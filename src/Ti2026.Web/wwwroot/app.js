/* ============================================================================
   TI 2026 — logic giao diện
   ----------------------------------------------------------------------------
   Không framework, không build step. Mọi đường dẫn API là TƯƠNG ĐỐI để trang
   chạy đúng cả khi đứng sau reverse proxy với prefix (/ti2026).
   ========================================================================= */

'use strict';

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

/**
 * Host ảnh hero. Phải là steamcdn-a.akamaihd.net, KHÔNG phải cdn.cloudflare.steamstatic.com.
 *
 * Bằng chứng thu được từ chính trang này, ba lần thất bại và hai lần thành công:
 *   cdn.cloudflare.steamstatic.com  → ảnh KHÔNG hiện (tier list, hero pool, và cả tab Học từ pro
 *                                     trước khi sửa) — dù curl từ VPS vẫn trả 200
 *   avatars.steamstatic.com         → ảnh KHÔNG hiện (avatar tuyển thủ)
 *   steamcdn-a.akamaihd.net         → hiện bình thường (logo đội, tab Học từ pro sau khi sửa)
 *   cdn.steamusercontent.com        → hiện bình thường (logo đội)
 *
 * Tức là *.steamstatic.com không tới được từ mạng người dùng. Không rõ vì sao — nhưng không cần
 * biết vì sao để chọn đúng: dùng host đã chứng minh chạy được.
 */
const HERO_CDN = 'https://steamcdn-a.akamaihd.net/apps/dota2/images/dota_react/heroes/';

/** Thoát HTML. Mọi giá trị từ API đều đi qua đây trước khi vào innerHTML. */
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => (
  { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
));

/**
 * Hiển thị một con số có thể chưa biết.
 * null/undefined -> "—", KHÔNG phải "0". Đây là quy ước xuyên suốt: API trả null
 * cho chỉ số mà nguồn dữ liệu chưa cung cấp, và hiển thị 0 ở đó là bịa số liệu.
 */
const fmt = (v, digits = 0, suffix = '') =>
  (v === null || v === undefined) ? '—' : v.toFixed(digits) + suffix;

const state = {
  teams: [],
  rosters: {},
  h2h: null,
  tiers: null,
  meta: {},
  tierPosition: 0,
  tierQuery: '',
  formWindow: 30,
  formMetric: 'winrate',
  formHidden: new Set(),
  sortKey: 'winrate',
  sortAsc: false,
};

/* ============================ Tải dữ liệu ============================ */

async function getJson(path) {
  const res = await fetch(path);

  if (!res.ok) {
    // Giữ lại thân phản hồi. Nhiều endpoint trả về câu giải thích cụ thể — "hồ sơ để riêng
    // tư" khác hẳn "tạm hết suất tra cứu" — và vứt nó đi để hiện "HTTP 404" trần trụi thì
    // người dùng không biết phải làm gì tiếp.
    const body = await res.text().catch(() => '');
    const err = new Error(`${path} trả về HTTP ${res.status}`);
    err.status = res.status;
    err.body = body;

    try {
      const parsed = JSON.parse(body);
      if (parsed && parsed.error) err.serverMessage = parsed.error;
    } catch { /* không phải JSON thì thôi */ }

    throw err;
  }

  return res.json();
}

async function boot() {
  try {
    const teamsDoc = await getJson('api/teams');
    state.teams = teamsDoc.teams || [];

    // Các nguồn phụ được phép hỏng mà không kéo cả trang xuống: thiếu tier list
    // thì chỉ tab tier list trống, không phải trang trắng.
    const [meta, rosters, h2h, tiers, players] = await Promise.all([
      getJson('api/meta').catch(() => ({})),
      getJson('api/rosters').then((d) => d.rosters || {}).catch(() => ({})),
      getJson('api/h2h').catch(() => null),
      getJson('api/tiers').catch(() => null),
      getJson('api/players').catch(() => null),
    ]);

    state.meta = meta;
    state.rosters = rosters;
    state.h2h = h2h;
    state.tiers = tiers;
    // pl.h là bảng tra cứu { heroId: [tên, mã ảnh] } — dùng để đặt tên cho hero pool
    state.heroLookup = (players && players.h) || {};

    renderStatus();
    renderOverview();
    renderTable();
    renderTeams();
    setupH2h();
    setupTiers();
    setupForm();
    setupPredict();
    setupChanges();
    setupPlayerStats();
    setupHeroPool();
  } catch (err) {
    const box = $('#global-error');
    box.hidden = false;
    box.className = 'error';
    box.innerHTML =
      'Không tải được <code>api/teams</code>. Kiểm tra <code>api/health</code> ' +
      'hoặc log của container.<br><small>' + esc(err.message) + '</small>';
  }
}

/* ============================ Trạng thái đầu trang ============================ */

function renderStatus() {
  const m = state.meta;
  const when = m.updatedAt
    ? new Date(m.updatedAt).toLocaleString('vi-VN', { dateStyle: 'medium', timeStyle: 'short' })
    : '—';

  $('#footer-updated').textContent = when;

  const withData = state.teams.filter((t) => t.stats).length;
  const parts = [`<b>${esc(when)}</b>`, `${withData}/${state.teams.length} đội có dữ liệu`];
  if (m.seed) parts.push('dữ liệu mồi');

  $('#status-text').innerHTML = parts.join(' · ');
  $('#status-chip .dot').classList.toggle('stale', Boolean(m.seed));
}

/* ============================ Tiện ích đội ============================ */

const withStats = () => state.teams.filter((t) => t.stats);

/**
 * Số ván tối thiểu để một đội được xét vào bảng "dẫn đầu".
 *
 * Không có ngưỡng này thì đội mới thành lập với 10-17 ván sẽ chiếm các ô KPI chỉ nhờ nhiễu
 * thống kê — và một con số dẫn đầu dựa trên 10 ván trông y hệt con số dựa trên 124 ván.
 * Đội dưới ngưỡng vẫn hiện đầy đủ ở bảng chỉ số và thẻ đội, chỉ không được tuyên bố là
 * "cao nhất giải".
 */
const MIN_MAPS_FOR_LEADERBOARD = 30;

const eligibleForLeaderboard = () =>
  withStats().filter((t) => t.stats.maps >= MIN_MAPS_FOR_LEADERBOARD);

const tierOf = (wr) => (wr >= 60 ? 'S' : wr >= 50 ? 'A' : 'B');

const TIER_COLOR = { S: '#e0574f', A: '#e2913f', B: '#4d7fd6' };

function teamLogo(team, size) {
  const initial = esc((team.name || '?').charAt(0));
  const cls = size ? ` style="width:${size}px;height:${size}px"` : '';
  if (!team.logo) return `<span class="logo-fallback"${cls}>${initial}</span>`;
  return `<img class="logo"${cls} src="${esc(team.logo)}" alt="" loading="lazy"
    onerror="this.outerHTML='<span class=&quot;logo-fallback&quot;>${initial}</span>'">`;
}

/* ============================ Tổng quan ============================ */

const KPIS = [
  {
    label: 'Winrate cao nhất', cls: 'p2',
    pick: (a, b) => b.stats.winrate - a.stats.winrate,
    value: (t) => fmt(t.stats.winrate, 0, '%'),
    note: (t) => `${t.stats.maps} ván đã đấu`,
  },
  {
    label: 'Áp đảo giao tranh', cls: 'p1',
    pick: (a, b) => b.stats.killDiff - a.stats.killDiff,
    value: (t) => (t.stats.killDiff > 0 ? '+' : '') + fmt(t.stats.killDiff, 2),
    note: () => 'chênh lệch K–D mỗi ván',
  },
  {
    label: 'Đóng trận chắc nhất', cls: 'p3',
    pick: (a, b) => (b.stats.winWhenF10 ?? -1) - (a.stats.winWhenF10 ?? -1),
    value: (t) => fmt(t.stats.winWhenF10, 0, '%'),
    note: () => 'thắng khi dẫn 10 kill đầu',
  },
  {
    label: 'Kết trận nhanh nhất', cls: 'p4',
    pick: (a, b) => a.stats.duration - b.stats.duration,
    value: (t) => fmt(t.stats.duration, 0, '′'),
    note: () => 'thời lượng trung bình',
  },
  {
    label: 'Trận nhiều máu nhất', cls: 'p5',
    pick: (a, b) => b.stats.totalKills - a.stats.totalKills,
    value: (t) => fmt(t.stats.totalKills, 1),
    note: () => 'tổng kills cả hai bên',
  },
];

function renderOverview() {
  const data = withStats();
  const grid = $('#kpi-grid');
  const ranking = $('#ranking');

  if (!data.length) {
    grid.innerHTML = '';
    ranking.innerHTML = '<div class="empty">Chưa có đội nào có dữ liệu chỉ số.</div>';
    return;
  }

  const pool = eligibleForLeaderboard();
  const excluded = data.length - pool.length;

  grid.innerHTML = (pool.length ? KPIS : []).map((k) => {
    const t = [...pool].sort(k.pick)[0];
    return `<article class="kpi ${k.cls}">
      <div class="kpi-label">${esc(k.label)}</div>
      <div class="kpi-value">${k.value(t)}</div>
      <div class="kpi-team">${esc(t.name)}</div>
      <div class="kpi-note">${esc(k.note(t))} · ${t.stats.maps} ván</div>
    </article>`;
  }).join('');

  $('#kpi-caveat').innerHTML = excluded > 0
    ? `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
       <div>Bảng dẫn đầu chỉ xét đội có từ <b>${MIN_MAPS_FOR_LEADERBOARD} ván</b> trở lên
       (${excluded} đội chưa đủ mẫu). Số dựa trên 10–20 ván trông y hệt số dựa trên 120 ván,
       nhưng độ tin cậy khác hẳn. Các đội đó vẫn hiện đầy đủ ở những mục khác.</div>`
    : '';
  $('#kpi-caveat').className = excluded > 0 ? 'note' : '';

  renderRanking(data);
}

/**
 * Thanh xếp hạng phân kỳ quanh mốc 50%.
 *
 * Bản đầu vẽ thanh tỉ lệ với winrate/max. Với dải thật 26–67%, mọi thanh đều dài 39–100%
 * nên nhìn gần như bằng nhau — biểu đồ không kể được câu chuyện nào. Cắt trục để phóng đại
 * chênh lệch thì lại là kiểu bóp méo kinh điển.
 *
 * Neo vào 50% giải quyết cả hai: 50% là mốc CÓ NGHĨA THẬT trong winrate (trên/dưới hoà),
 * nên độ dài thanh đọc thẳng ra là "hơn hoà bao nhiêu", và hai phía tách nhau rõ ràng.
 */
function renderRanking(data) {
  const sorted = [...data].sort((a, b) => b.stats.winrate - a.stats.winrate);
  const spread = Math.max(...sorted.map((t) => Math.abs(t.stats.winrate - 50)), 1);

  $('#ranking').innerHTML = sorted.map((t, i) => {
    const wr = t.stats.winrate;
    const diff = wr - 50;
    const width = (Math.abs(diff) / spread) * 100;
    const tier = tierOf(wr);
    const thin = t.stats.maps < MIN_MAPS_FOR_LEADERBOARD;

    return `<div class="rank-row">
      <span class="rank-no num">${i + 1}</span>
      <div class="rank-team">
        ${teamLogo(t)}
        <span class="rank-name">${esc(t.name)}</span>
        ${thin ? `<span class="rank-thin" title="Ít ván nên chỉ số dao động mạnh">${t.stats.maps} ván</span>` : ''}
      </div>
      <div class="rank-track">
        <div class="rank-half left">${diff < 0 ? `<span class="rank-bar neg" style="width:${width}%"></span>` : ''}</div>
        <div class="rank-axis" aria-hidden="true"></div>
        <div class="rank-half right">${diff >= 0 ? `<span class="rank-bar pos" style="width:${width}%"></span>` : ''}</div>
      </div>
      <span class="rank-val num">
        <span class="tier-badge" style="background:${TIER_COLOR[tier]}">${tier}</span>
        ${fmt(wr, 0, '%')}
      </span>
    </div>`;
  }).join('') + `<p class="desc" style="margin-top:var(--s-3);text-align:center">
      Vạch giữa là mốc hoà 50%. Thanh sang phải là thắng nhiều hơn thua.</p>`;
}

/* ============================ Bảng chỉ số ============================ */

const COLUMNS = [
  { key: 'name', label: 'Đội', type: 'text' },
  { key: 'tier', label: 'Tier', type: 'tier' },
  { key: 'maps', label: 'Ván', d: 0 },
  { key: 'winrate', label: 'WR%', d: 0, suffix: '%' },
  { key: 'kills', label: 'Kills', d: 2 },
  { key: 'deaths', label: 'Deaths', d: 2, invert: true },
  { key: 'killDiff', label: 'Chênh K–D', d: 2, sign: true },
  { key: 'totalKills', label: 'Tổng kills', d: 1 },
  { key: 'assists', label: 'Assists', d: 1 },
  { key: 'firstBlood', label: 'FB%', d: 0, suffix: '%' },
  { key: 'f10', label: 'F10%', d: 0, suffix: '%' },
  { key: 'winWhenFb', label: 'Thắng|FB', d: 0, suffix: '%' },
  { key: 'winWhenF10', label: 'Thắng|F10', d: 0, suffix: '%' },
  { key: 'duration', label: 'Phút', d: 0 },
];

const cellValue = (t, key) =>
  key === 'name' ? t.name
    : key === 'tier' ? (t.stats ? tierOf(t.stats.winrate) : null)
      : t.stats ? t.stats[key] : null;

function heatStyle(key, value) {
  if (value === null || value === undefined) return '';
  const col = COLUMNS.find((c) => c.key === key);
  if (!col || col.type) return '';

  const values = withStats().map((t) => t.stats[key]).filter((v) => v !== null && v !== undefined);
  if (values.length < 2) return '';

  const min = Math.min(...values);
  const max = Math.max(...values);
  let f = (value - min) / (max - min || 1);
  if (col.invert) f = 1 - f;

  return `background:color-mix(in srgb, var(--heat-hi) ${(f * 62).toFixed(0)}%, var(--heat-lo))`;
}

function renderTable() {
  const note = $('#stats-note');
  const sources = new Set(withStats().map((t) => t.stats.source).filter(Boolean));

  // Nói rõ khi số đang hiển thị là số biên tập chứ không phải số đo từ match thật.
  if (sources.has('editorial') || sources.has('mixed')) {
    note.className = 'note';
    note.innerHTML =
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>' +
      '<div>Một phần chỉ số đang là <b>dữ liệu biên tập</b> nhập tay, chưa phải số đo từ match thật. ' +
      'Cột hiện <b>—</b> là chỉ số nguồn chưa cung cấp, không phải bằng 0.</div>';
  } else {
    note.className = '';
    note.innerHTML = '';
  }

  const rows = [...state.teams].sort((a, b) => {
    const x = cellValue(a, state.sortKey);
    const y = cellValue(b, state.sortKey);
    if (x === null || x === undefined) return 1;
    if (y === null || y === undefined) return -1;
    if (typeof x === 'string') return state.sortAsc ? x.localeCompare(y) : y.localeCompare(x);
    return state.sortAsc ? x - y : y - x;
  });

  const head = COLUMNS.map((c) => {
    const active = c.key === state.sortKey;
    const sortAttr = active ? ` aria-sort="${state.sortAsc ? 'ascending' : 'descending'}"` : '';
    const arrow = active ? (state.sortAsc ? '▲' : '▼') : '↕';
    return `<th scope="col" data-key="${c.key}"${sortAttr} tabindex="0">${esc(c.label)}<span class="sort">${arrow}</span></th>`;
  }).join('');

  const body = rows.map((t) => COLUMNS.map((c) => {
    const v = cellValue(t, c.key);

    if (c.type === 'text') {
      return `<td><div class="team-cell">${teamLogo(t)}<span>${esc(t.name)}</span></div></td>`;
    }
    if (c.type === 'tier') {
      return v
        ? `<td><span class="tier-badge" style="background:${TIER_COLOR[v]}">${v}</span></td>`
        : '<td class="na">—</td>';
    }
    if (v === null || v === undefined) return '<td class="na">—</td>';

    const text = (c.sign && v > 0 ? '+' : '') + fmt(v, c.d, c.suffix || '');
    return `<td class="num" style="${heatStyle(c.key, v)}">${text}</td>`;
  }).join('')).map((tds) => `<tr>${tds}</tr>`).join('');

  $('#stats-table').innerHTML =
    `<thead><tr>${head}</tr></thead><tbody>${body}</tbody>`;

  $$('#stats-table th').forEach((th) => {
    const activate = () => {
      const key = th.dataset.key;
      if (key === state.sortKey) state.sortAsc = !state.sortAsc;
      else { state.sortKey = key; state.sortAsc = false; }
      renderTable();
    };
    th.onclick = activate;
    th.onkeydown = (e) => {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); activate(); }
    };
  });
}

/* ============================ Đội hình ============================ */

const ROLE_META = {
  CORE: ['1', 'Carry'],
  MID: ['2', 'Mid'],
  OFFLANE: ['3', 'Offlane'],
  SUPPORT: ['4', 'Hỗ trợ'],
  'FULL SUPPORT': ['5', 'Hỗ trợ chính'],
  COACH: ['HLV', 'Huấn luyện viên'],
};
const ROLE_ORDER = { CORE: 1, MID: 2, OFFLANE: 3, SUPPORT: 4, 'FULL SUPPORT': 5, COACH: 6 };

function teamTags(s) {
  const out = [];
  if (s.winrate >= 65) out.push(['Phong độ đỉnh cao', 0]);
  if (s.killDiff >= 5) out.push(['Áp đảo giao tranh', 0]);
  if ((s.firstBlood ?? 0) >= 55 || (s.f10 ?? 0) >= 62) out.push(['Áp nhịp sớm', 0]);
  if ((s.winWhenF10 ?? 0) >= 85) out.push(['Đóng trận kỷ luật', 0]);
  if (s.duration <= 41) out.push(['Kết trận nhanh', 0]);
  if (s.killDiff <= -3) out.push(['Bị áp đảo', 1]);
  if ((s.winWhenFb ?? 100) <= 45) out.push(['Chuyển hoá kém', 1]);
  if (s.winrate < 45) out.push(['Đang sa sút', 1]);
  return out.slice(0, 4);
}

function renderSquad(slug) {
  const members = state.rosters[slug];
  if (!members || !members.length) return '';

  const sorted = [...members].sort(
    (a, b) => (ROLE_ORDER[a.role] || 9) - (ROLE_ORDER[b.role] || 9));

  return `<div class="squad">${sorted.map((p) => {
    const meta = ROLE_META[p.role] || ['', ''];
    const isCoach = p.role === 'COACH';
    const initial = esc((p.nick || '?').charAt(0));
    const img = p.photo
      ? `<img src="${esc(p.photo)}" alt="" loading="lazy" onerror="this.remove()">`
      : '';
    return `<div class="player">
      <div class="avatar${isCoach ? ' coach' : ''}" title="${esc(meta[1])}">
        ${initial}${img}<span class="role">${esc(meta[0])}</span>
      </div>
      <div class="nick" title="${esc(p.nick)}">${esc(p.nick)}</div>
      <div class="real" title="${esc(p.real || '')}">${esc(p.real || ' ')}</div>
    </div>`;
  }).join('')}</div>`;
}

function renderTeams() {
  const sorted = [...state.teams].sort((a, b) => {
    if (a.stats && b.stats) return b.stats.winrate - a.stats.winrate;
    return a.stats ? -1 : b.stats ? 1 : 0;
  });

  $('#team-grid').innerHTML = sorted.map((t) => {
    const s = t.stats;
    const stats = s ? `
      <div class="tags">${teamTags(s).map(([label, warn]) =>
        `<span class="tag${warn ? ' warn' : ''}">${esc(label)}</span>`).join('')}</div>
      <div class="mini-stats">
        <div><div class="label">Winrate</div><div class="stat-value">${fmt(s.winrate, 0, '%')}</div></div>
        <div><div class="label">Chênh K–D</div><div class="stat-value">${(s.killDiff > 0 ? '+' : '') + fmt(s.killDiff, 2)}</div></div>
        <div><div class="label">Ván</div><div class="stat-value">${fmt(s.maps)}</div></div>
      </div>`
      : '<div class="empty" style="padding:var(--s-3);margin-top:var(--s-3)">Chưa có dữ liệu chỉ số.</div>';

    return `<article class="team-card">
      <header>
        ${teamLogo(t)}
        <div>
          <h3>${esc(t.name)}</h3>
          <div class="region">${esc(t.region || '')}${t.qualification ? ' · ' + esc(t.qualification) : ''}</div>
        </div>
      </header>
      ${stats}
      ${renderSquad(t.slug)}
    </article>`;
  }).join('');
}

/* ============================ Đối đầu ============================ */

const H2H_METRICS = [
  { key: 'winrate', label: 'Winrate', unit: '%', d: 0, higher: true },
  { key: 'kills', label: 'Kills / ván', unit: '', d: 2, higher: true },
  { key: 'deaths', label: 'Deaths / ván', unit: '', d: 2, higher: false },
  { key: 'killDiff', label: 'Chênh K–D', unit: '', d: 2, higher: true },
  { key: 'totalKills', label: 'Tổng kills', unit: '', d: 1, higher: true },
  { key: 'firstBlood', label: 'First blood', unit: '%', d: 0, higher: true },
  { key: 'f10', label: 'Dẫn 10 kill đầu', unit: '%', d: 0, higher: true },
  { key: 'winWhenFb', label: 'Thắng khi có FB', unit: '%', d: 0, higher: true },
  { key: 'winWhenF10', label: 'Thắng khi dẫn F10', unit: '%', d: 0, higher: true },
  { key: 'duration', label: 'Thời lượng', unit: '′', d: 0, higher: false },
];

function setupH2h() {
  const data = withStats();
  const a = $('#h2h-a');
  const b = $('#h2h-b');

  if (data.length < 2) {
    $('#h2h-body').innerHTML =
      '<div class="empty">Cần ít nhất hai đội có dữ liệu để so sánh.</div>';
    a.disabled = b.disabled = true;
    return;
  }

  const options = data.map((t) => `<option value="${esc(t.slug)}">${esc(t.name)}</option>`).join('');
  a.innerHTML = options;
  b.innerHTML = options;
  a.value = data[0].slug;
  b.value = data[1].slug;

  a.onchange = b.onchange = renderH2h;
  renderH2h();
}

function renderH2h() {
  const A = state.teams.find((t) => t.slug === $('#h2h-a').value);
  const B = state.teams.find((t) => t.slug === $('#h2h-b').value);
  if (!A || !B || !A.stats || !B.stats) return;

  const rows = H2H_METRICS.map((m) => {
    const va = A.stats[m.key];
    const vb = B.stats[m.key];

    // Chỉ số một trong hai bên chưa biết thì KHÔNG tuyên bố ai hơn.
    const known = va !== null && va !== undefined && vb !== null && vb !== undefined;
    const aWins = known && (m.higher ? va > vb : va < vb);
    const bWins = known && (m.higher ? vb > va : vb < va);

    const span = known ? Math.max(Math.abs(va), Math.abs(vb), 0.001) : 1;
    const wa = known ? Math.max((Math.abs(va) / span) * 100, 3) : 0;
    const wb = known ? Math.max((Math.abs(vb) / span) * 100, 3) : 0;

    return `<div class="h2h-row">
      <div class="h2h-bar-wrap left">
        <span class="h2h-val num${aWins ? ' win' : ''}">${aWins ? '● ' : ''}${fmt(va, m.d, m.unit)}</span>
        <span class="h2h-bar a" style="width:${wa}%"></span>
      </div>
      <div class="h2h-metric">${esc(m.label)}<small>${m.higher ? 'cao hơn tốt hơn' : 'thấp hơn tốt hơn'}</small></div>
      <div class="h2h-bar-wrap">
        <span class="h2h-bar b" style="width:${wb}%"></span>
        <span class="h2h-val num${bWins ? ' win' : ''}">${fmt(vb, m.d, m.unit)}${bWins ? ' ●' : ''}</span>
      </div>
    </div>`;
  }).join('');

  const pairs = state.h2h && state.h2h.pairs ? state.h2h.pairs : {};
  const key = [A.slug, B.slug].sort().join('|');
  const series = pairs[key];

  const history = series && series.series && series.series.length
    ? `<div style="margin-top:var(--s-5);padding-top:var(--s-4);border-top:1px solid var(--border)">
         <h3 style="font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:var(--s-3)">
           Lịch sử đối đầu · ${series.n} trận</h3>
         ${series.series.slice(0, 10).map((row) => `<div class="h2h-row" style="grid-template-columns:1fr auto 1fr;padding:7px 0;border-bottom:1px solid var(--border)">
             <span style="font-size:12.5px;color:var(--ink-2)">${esc(row[0])}</span>
             <span class="num" style="font-weight:700">${esc(row[2])} – ${esc(row[3])}</span>
             <span style="font-size:11.5px;color:var(--muted);text-align:right">${esc(row[1])}</span>
           </div>`).join('')}
       </div>`
    : `<div class="empty" style="margin-top:var(--s-4)">Chưa có lịch sử đối đầu giữa hai đội này.
         Dữ liệu trận sẽ có sau khi pipeline nạp được từ OpenDota.</div>`;

  $('#h2h-body').innerHTML = `
    <div class="h2h-head">
      <div class="h2h-side">${teamLogo(A)}<div><strong>${esc(A.name)}</strong><div class="region" style="font-size:12px;color:var(--muted)">${esc(A.region || '')}</div></div></div>
      <span class="h2h-vs">VS</span>
      <div class="h2h-side right">${teamLogo(B)}<div><strong>${esc(B.name)}</strong><div class="region" style="font-size:12px;color:var(--muted)">${esc(B.region || '')}</div></div></div>
    </div>
    ${rows}${history}`;
}

/* ============================ Tier list ============================ */

function setupTiers() {
  const t = state.tiers;
  if (!t || !t.positions) {
    $('#tier-body').innerHTML = '<div class="empty">Chưa tải được <code>api/tiers</code>.</div>';
    return;
  }

  $('#tier-patch').textContent =
    `Patch ${t.patch || '—'}${t.patchDate ? ' · ' + t.patchDate : ''}`;

  const positions = Object.entries(t.positions);
  $('#tier-positions').innerHTML = positions.map(([idx, p]) =>
    `<button class="pill" type="button" data-pos="${esc(idx)}"
       aria-pressed="${Number(idx) === state.tierPosition}">${esc(p.short || p.label)}</button>`
  ).join('');

  $$('#tier-positions .pill').forEach((btn) => {
    btn.onclick = () => {
      state.tierPosition = Number(btn.dataset.pos);
      $$('#tier-positions .pill').forEach((b) =>
        b.setAttribute('aria-pressed', String(b === btn)));
      renderTiers();
    };
  });

  $('#tier-search').oninput = (e) => {
    state.tierQuery = e.target.value.trim().toLowerCase();
    renderTiers();
  };

  if (t.caveat) {
    $('#tier-caveat').className = 'note';
    $('#tier-caveat').innerHTML =
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>' +
      `<div>${esc(t.caveat)}</div>`;
  }

  renderTiers();
}

function renderTiers() {
  const t = state.tiers;
  const pos = t.positions[state.tierPosition];
  if (!pos) return;

  const q = state.tierQuery;

  const rows = (t.tierOrder || []).map((tierKey) => {
    const heroes = (pos.tiers && pos.tiers[tierKey]) || [];
    if (!heroes.length) return '';

    const meta = (t.tierMeta && t.tierMeta[tierKey]) || { n: tierKey, d: '', c: '#999' };

    const cards = heroes.map((h) => {
      const dim = q && !h.n.toLowerCase().includes(q) ? ' dim' : '';
      const title = `${h.n}${h.w ? ` · winrate pub ${h.w}%` : ''}${h.t ? `\n${h.t}` : ''}`;
      return `<div class="hero${dim}" title="${esc(title)}">
        <img src="${HERO_CDN}crops/${esc(h.i)}.png" alt="${esc(h.n)}" loading="lazy"
             onerror="this.closest('.hero').classList.add('noimg');this.remove()">
        <span class="name">${esc(h.n)}</span>
        <span class="fallback">${esc(h.n)}</span>
      </div>`;
    }).join('');

    return `<div class="tier-row">
      <div class="tier-label" style="background:${esc(meta.c)}"
           title="${esc(meta.d || '')}">${esc(meta.n || tierKey)}<small>${esc((meta.d || '').split('—')[0].trim())}</small></div>
      <div class="hero-strip">${cards}</div>
    </div>`;
  }).join('');

  $('#tier-body').innerHTML =
    `<p class="desc" style="margin-bottom:var(--s-3)">${esc(pos.label || '')} — ${esc(pos.desc || '')}</p>${rows}`;
}

/* ============================ Phong độ ============================ */

const SERIES_COLORS = [
  '#5b4ee0', '#0f8f68', '#cf3a40', '#2a4f96', '#9a4a24',
  '#8a5f14', '#5c3fa8', '#17694c', '#c2436f', '#3f7a8c',
  '#a0522d', '#4b6cb7', '#7a3e9d', '#2f8f5b', '#b8860b', '#6d5b8c',
];

function setupForm() {
  $$('#form-controls .pill').forEach((btn) => {
    btn.onclick = () => {
      state.formWindow = Number(btn.dataset.window);
      $$('#form-controls .pill').forEach((b) =>
        b.setAttribute('aria-pressed', String(b === btn)));
      loadForm();
    };
  });

  $('#form-metric').onchange = (e) => {
    state.formMetric = e.target.value;
    loadForm();
  };

  loadForm();
}

async function loadForm() {
  const body = $('#form-body');
  body.innerHTML = '<div class="skeleton" style="height:280px"></div>';

  try {
    const rows = await getJson(`api/trend?window=${state.formWindow}`);
    renderForm(rows);
  } catch (err) {
    body.className = '';
    body.innerHTML = `<div class="error">Không tải được <code>api/trend</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

function renderForm(rows) {
  const body = $('#form-body');
  const metric = state.formMetric;

  if (!rows || rows.length === 0) {
    body.innerHTML = `<div class="empty">
      <p style="margin:0 0 var(--s-2)"><strong>Chưa có đủ dữ liệu để vẽ biểu đồ.</strong></p>
      <p style="margin:0">Biểu đồ phong độ cần snapshot của <b>nhiều ngày khác nhau</b>.
      Pipeline ghi một snapshot mỗi ngày, nên đường biểu diễn sẽ hình thành dần sau vài ngày chạy.</p>
    </div>`;
    return;
  }

  // Gom theo đội, mỗi đội một chuỗi điểm theo ngày
  const byTeam = new Map();
  for (const r of rows) {
    if (!byTeam.has(r.teamSlug)) byTeam.set(r.teamSlug, { name: r.teamName, points: [] });
    const v = r[metric];
    if (v === null || v === undefined) continue;
    byTeam.get(r.teamSlug).points.push({ date: r.date, value: v });
  }

  const series = [...byTeam.entries()]
    .filter(([, s]) => s.points.length > 0)
    .map(([slug, s], i) => ({ slug, name: s.name, points: s.points, color: SERIES_COLORS[i % SERIES_COLORS.length] }));

  if (!series.length) {
    body.innerHTML = `<div class="empty">Chỉ số này chưa có dữ liệu — nguồn chưa cung cấp.</div>`;
    return;
  }

  const dates = [...new Set(rows.map((r) => r.date))].sort();
  const allValues = series.flatMap((s) => s.points.map((p) => p.value));
  const minV = Math.min(...allValues);
  const maxV = Math.max(...allValues);
  const pad = (maxV - minV) * 0.12 || 1;
  const lo = minV - pad;
  const hi = maxV + pad;

  const W = 900, H = 320, ML = 46, MR = 16, MT = 16, MB = 34;
  const plotW = W - ML - MR;
  const plotH = H - MT - MB;

  const xOf = (date) => {
    const i = dates.indexOf(date);
    return dates.length === 1 ? ML + plotW / 2 : ML + (i / (dates.length - 1)) * plotW;
  };
  const yOf = (v) => MT + plotH - ((v - lo) / (hi - lo || 1)) * plotH;

  const ticks = 4;
  const gridlines = Array.from({ length: ticks + 1 }, (_, i) => {
    const v = lo + ((hi - lo) * i) / ticks;
    const y = yOf(v);
    return `<line class="grid-line" x1="${ML}" y1="${y.toFixed(1)}" x2="${W - MR}" y2="${y.toFixed(1)}"/>
      <text class="axis-text" x="${ML - 8}" y="${(y + 4).toFixed(1)}" text-anchor="end">${v.toFixed(v > 20 ? 0 : 1)}</text>`;
  }).join('');

  const labelStep = Math.max(1, Math.ceil(dates.length / 6));
  const xLabels = dates.map((d, i) => (i % labelStep === 0 || i === dates.length - 1)
    ? `<text class="axis-text" x="${xOf(d).toFixed(1)}" y="${H - 10}" text-anchor="middle">${d.slice(5)}</text>`
    : '').join('');

  const paths = series.filter((s) => !state.formHidden.has(s.slug)).map((s) => {
    // Ngày khuyết thì NGẮT đường: bắt đầu một <path> mới thay vì nối thẳng qua.
    // Nối thẳng qua khoảng trống sẽ vẽ ra một xu hướng không hề được đo.
    const segments = [];
    let current = [];
    let prevIndex = -99;

    for (const p of s.points.slice().sort((a, b) => a.date.localeCompare(b.date))) {
      const idx = dates.indexOf(p.date);
      if (idx !== prevIndex + 1 && current.length) { segments.push(current); current = []; }
      current.push(p);
      prevIndex = idx;
    }
    if (current.length) segments.push(current);

    const lines = segments.filter((seg) => seg.length > 1).map((seg) =>
      `<path class="series" stroke="${s.color}" d="${seg.map((p, i) =>
        `${i === 0 ? 'M' : 'L'}${xOf(p.date).toFixed(1)},${yOf(p.value).toFixed(1)}`).join(' ')}"/>`
    ).join('');

    const dots = s.points.map((p) =>
      `<circle class="point" cx="${xOf(p.date).toFixed(1)}" cy="${yOf(p.value).toFixed(1)}" r="3.5"
        fill="${s.color}"><title>${esc(s.name)} · ${esc(p.date)} · ${p.value}</title></circle>`
    ).join('');

    return lines + dots;
  }).join('');

  const singleDay = dates.length === 1
    ? `<div class="note"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
       <div>Mới có <b>một ngày</b> dữ liệu nên chưa vẽ được đường xu hướng. Mỗi ngày pipeline chạy sẽ thêm một điểm.</div></div>`
    : '';

  body.innerHTML = `${singleDay}
    <div class="chart-wrap">
      <svg class="chart" viewBox="0 0 ${W} ${H}" role="img"
           aria-label="Biểu đồ ${esc(metric)} theo thời gian của ${series.length} đội">
        ${gridlines}${xLabels}${paths}
      </svg>
    </div>
    <div class="legend">${series.map((s) =>
      `<button type="button" data-slug="${esc(s.slug)}" aria-pressed="${!state.formHidden.has(s.slug)}">
         <span class="swatch" style="background:${s.color}"></span>${esc(s.name)}
       </button>`).join('')}</div>
    <p class="desc" style="margin-top:var(--s-3)">Bấm vào tên đội để ẩn/hiện đường của đội đó.</p>`;

  $$('#form-body .legend button').forEach((btn) => {
    btn.onclick = () => {
      const slug = btn.dataset.slug;
      if (state.formHidden.has(slug)) state.formHidden.delete(slug);
      else state.formHidden.add(slug);
      renderForm(rows);
    };
  });
}

/* ============================ Dự đoán ============================ */

async function setupPredict() {
  const body = $('#ratings-body');
  body.innerHTML = '<div class="skeleton" style="height:180px"></div>';

  try {
    const ratings = await getJson('api/ratings');
    renderRatings(ratings);
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/ratings</code>.<br><small>${esc(err.message)}</small></div>`;
  }

  const data = withStats();
  if (data.length < 2) {
    $('#predict-body').innerHTML = '<div class="empty">Cần ít nhất hai đội có dữ liệu.</div>';
    return;
  }

  const options = data.map((t) => `<option value="${esc(t.slug)}">${esc(t.name)}</option>`).join('');
  const a = $('#pred-a');
  const b = $('#pred-b');
  a.innerHTML = options;
  b.innerHTML = options;
  a.value = data[0].slug;
  b.value = data[1].slug;
  a.onchange = b.onchange = loadPredict;

  loadPredict();
  loadCalibration();
  loadPatches();
}

async function loadCalibration() {
  const body = $('#calibration-body');
  body.innerHTML = '<div class="skeleton" style="height:180px"></div>';

  try {
    const c = await getJson('api/calibration');

    if (!c.evaluated) {
      body.innerHTML = `<div class="empty">${esc(c.note || 'Chưa đủ dữ liệu để hiệu chuẩn.')}</div>`;
      return;
    }

    // Brier 0.25 là mốc tung đồng xu. Vẽ khoảng cách tới mốc đó thay vì con số trần trụi,
    // vì "0.2419" tự nó không nói lên điều gì với người đọc.
    const edge = Math.max(0, (0.25 - c.brierScore) / 0.25 * 100);

    const rows = c.buckets.map((b) => {
      const gap = b.actual - b.predicted;
      const tone = Math.abs(gap) <= 3 ? 'good' : Math.abs(gap) <= 8 ? 'mid' : 'bad';
      return `<tr>
        <td>${esc(b.range)}</td>
        <td class="num">${b.predicted}%</td>
        <td class="num">${b.actual}%</td>
        <td class="num cal-${tone}">${gap > 0 ? '+' : ''}${gap.toFixed(1)}</td>
        <td class="num">${b.samples}</td>
      </tr>`;
    }).join('');

    // Chênh lệch giữa tập huấn luyện và tập kiểm định CHÍNH LÀ mức quá khớp. Hiện nó ra
    // thay vì giấu đi: nếu một ngày nào đó nó doãng ra thì phải nhìn thấy ngay.
    const inSample = c.inSample || {};
    const overfit = Number.isFinite(inSample.brierScore)
      ? c.brierScore - inSample.brierScore
      : null;

    body.innerHTML = `
      <div class="bento">
        <article class="kpi p2">
          <div class="kpi-label">Đoán đúng kèo trên</div>
          <div class="kpi-value">${c.hitRate}%</div>
          <div class="kpi-note">trên ${c.evaluated} trận chưa từng dùng để chỉnh tham số</div>
        </article>
        <article class="kpi p3">
          <div class="kpi-label">Hơn tung đồng xu</div>
          <div class="kpi-value">${edge.toFixed(1)}%</div>
          <div class="kpi-note">Brier ${c.brierScore} · 0.25 = ngẫu nhiên</div>
        </article>
        ${overfit === null ? '' : `
        <article class="kpi p1">
          <div class="kpi-label">Mức quá khớp</div>
          <div class="kpi-value">${overfit > 0 ? '+' : ''}${overfit.toFixed(4)}</div>
          <div class="kpi-note">kiểm định ${c.brierScore} so với huấn luyện ${inSample.brierScore}</div>
        </article>`}
      </div>

      <div class="table-scroll"><table>
        <caption class="sr-only">Đối chiếu xác suất mô hình đưa ra với kết quả thực tế</caption>
        <thead><tr>
          <th scope="col">Mức dự đoán</th><th scope="col">Mô hình nói</th>
          <th scope="col">Thực tế</th><th scope="col">Lệch</th><th scope="col">Mẫu</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div><b>Đọc con số này thế nào:</b> mô hình đã được hiệu chuẩn nên "65%" thật sự
        có nghĩa là khoảng 65%. Nhưng ưu thế so với đoán ngẫu nhiên chỉ khoảng
        ${edge.toFixed(0)}% — Dota biến động cao, và chênh lệch Elo không chuyển thành
        chắc thắng. Đừng đặt nặng hơn mức đó.</div>
      </div>

      ${c.parameterDrift && c.parameterDrift.drifted && c.bestOnTrain ? `
      <div class="note warn" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div><b>Tham số đã lệch khỏi dữ liệu.</b> Dữ liệu mới nhất chọn thang
        ${c.bestOnTrain.probabilityScale} và hệ số quên ${c.bestOnTrain.patchRegression},
        tốt hơn ${c.parameterDrift.gain} Brier so với mức đang chạy
        (${c.inUse.probabilityScale} / ${c.inUse.patchRegression}). Đến lúc đo lại và cập nhật hằng số.</div>
      </div>` : ''}`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/calibration</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

async function loadPatches() {
  const body = $('#patches-body');
  if (!body) return;
  body.innerHTML = '<div class="skeleton" style="height:140px"></div>';

  try {
    const p = await getJson('api/patches');
    const list = p.patches || [];

    if (!list.length) {
      body.innerHTML = '<div class="empty">Chưa có trận nào biết bản game.</div>';
      return;
    }

    const max = Math.max(...list.map((x) => x.matches));
    const total = list.reduce((s, x) => s + x.matches, 0);

    const rows = list.map((x) => {
      const pct = Math.round((x.matches / max) * 100);
      const current = x.stepsBehind === 0;
      return `<tr${current ? ' class="row-current"' : ''}>
        <td><b>${esc(x.name)}</b>${current ? ' <span class="chip">đang chạy</span>' : ''}</td>
        <td class="num">${x.matches}</td>
        <td><div class="minibar"><span style="width:${pct}%"></span></div></td>
        <td class="num">${x.weight === 1 ? '100%' : Math.round(x.weight * 100) + '%'}</td>
      </tr>`;
    }).join('');

    const off = !p.patchRegression;

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Số trận theo từng bản game và sức nặng tương ứng</caption>
        <thead><tr>
          <th scope="col">Bản</th><th scope="col">Số ván</th>
          <th scope="col">Tỷ trọng</th><th scope="col">Sức nặng</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${off
          ? `<b>Mọi bản đang tính đủ sức nặng — và đó là kết luận từ phép đo, không phải mặc định bỏ quên.</b>
             Đã thử giảm sức nặng dữ liệu bản cũ ở nhiều mức, kể cả vứt hẳn mọi trận trước
             ${esc(p.currentPatch || 'bản hiện tại')}: không mức nào cải thiện được độ chính xác
             ngoài mẫu, và vứt hẳn lịch sử còn hơi tệ hơn. Lý do là Elo vốn đã tự quên — một
             trận từ hai năm trước đã bị hàng trăm trận sau đó ghi đè.`
          : `Mỗi khi game lên bản chính mới, khoảng cách rating giữa các đội bị kéo lại
             ${Math.round(p.patchRegression * 100)}%, nên dữ liệu càng cũ càng nhẹ.`}
        <br><br><b>Lưu ý về dữ liệu:</b> ${esc(p.note || '')} Tổng ${total} ván.</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/patches</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

function renderRatings(rows) {
  if (!rows || !rows.length) {
    $('#ratings-body').innerHTML =
      '<div class="empty">Chưa có Elo. Cần ít nhất một vòng ingest có trận đấu.</div>';
    return;
  }

  const max = Math.max(...rows.map((r) => r.elo));
  const min = Math.min(...rows.map((r) => r.elo));
  const span = Math.max(max - min, 1);

  $('#ratings-body').innerHTML = rows.map((r, i) => {
    const width = Math.max(((r.elo - min) / span) * 100, 3);
    // Đội ít ván: Elo dao động mạnh vì mỗi trận đổi tới 24 điểm
    const thin = r.maps < MIN_MAPS_FOR_LEADERBOARD;
    return `<div class="rank-row">
      <span class="rank-no num">${i + 1}</span>
      <div class="rank-team">
        ${teamLogo({ name: r.teamName, logo: r.logo })}
        <span class="rank-name">${esc(r.teamName)}</span>
        ${thin ? `<span class="rank-thin" title="Ít ván nên Elo còn dao động">${r.maps} ván</span>` : ''}
      </div>
      <div class="elo-track"><span class="elo-bar" style="width:${width}%"></span></div>
      <span class="rank-val num">${r.elo}<small class="elo-wr">${r.winrate}%</small></span>
    </div>`;
  }).join('') + `<p class="desc" style="margin-top:var(--s-3)">
      Chênh 100 điểm Elo ≈ 64% cơ hội thắng; chênh 200 điểm ≈ 76%.
      Cột phải là winrate thô để bạn thấy hai thước đo lệch nhau ở đâu.</p>`;
}

async function loadPredict() {
  const a = $('#pred-a').value;
  const b = $('#pred-b').value;
  const body = $('#predict-body');

  if (a === b) {
    body.innerHTML = '<div class="empty">Chọn hai đội khác nhau.</div>';
    return;
  }

  body.innerHTML = '<div class="skeleton" style="height:240px"></div>';

  try {
    renderPredict(await getJson(`api/predict?a=${encodeURIComponent(a)}&b=${encodeURIComponent(b)}`));
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được dự đoán.<br><small>${esc(err.message)}</small></div>`;
  }
}

function renderPredict(p) {
  const pa = p.probabilityA;
  const pb = p.probabilityB;

  const head = pa === null
    ? '<div class="empty">Chưa đủ dữ liệu Elo cho một trong hai đội.</div>'
    : `<div class="pred-head">
         <div class="pred-side">
           ${teamLogo({ name: p.teamA.name, logo: p.teamA.logo }, 40)}
           <div><strong>${esc(p.teamA.name)}</strong><div class="pred-elo">Elo ${Math.round(p.teamA.elo)}</div></div>
         </div>
         <div class="pred-odds">
           <div class="pred-pct num">${fmt(pa, 1, '%')}</div>
           <div class="pred-split" role="img" aria-label="Xác suất ${pa}% so với ${pb}%">
             <span style="width:${pa}%"></span><span style="width:${pb}%"></span>
           </div>
           <div class="pred-pct num right">${fmt(pb, 1, '%')}</div>
         </div>
         <div class="pred-side right">
           <div><strong>${esc(p.teamB.name)}</strong><div class="pred-elo">Elo ${Math.round(p.teamB.elo)}</div></div>
           ${teamLogo({ name: p.teamB.name, logo: p.teamB.logo }, 40)}
         </div>
       </div>`;

  const caveat = p.confidence.lowSample
    ? `<div class="note"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
       <div>${esc(p.confidence.note)} Chỉ dựa trên ${p.confidence.matchesConsidered} trận.</div></div>`
    : '';

  const h2h = p.headToHead.played > 0
    ? `<div class="pred-block">
         <h3>Đối đầu trực tiếp</h3>
         <p class="pred-h2h num"><b>${p.headToHead.aWins}</b> – <b>${p.headToHead.bWins}</b>
            <span class="mu">sau ${p.headToHead.played} ván</span></p>
         ${p.headToHead.recent.map((m) => `<div class="pred-hist">
             <span>${esc(m.date)}</span>
             <span class="${m.aWon ? 'pos' : 'neg'}">${m.aWon ? esc(p.teamA.name) : esc(p.teamB.name)} thắng</span>
             <span class="num">${esc(m.score)}</span>
           </div>`).join('')}
       </div>`
    : '<div class="pred-block"><h3>Đối đầu trực tiếp</h3><p class="desc">Hai đội chưa từng gặp nhau trong dữ liệu.</p></div>';

  $('#predict-body').innerHTML = head + caveat + `
    <div class="pred-grid">
      ${lineBlock('Tổng kills mỗi ván', p.totalKills, '')}
      ${lineBlock('Thời lượng mỗi ván', p.duration, '′')}
    </div>
    ${h2h}`;

  function lineBlock(title, section, unit) {
    const d = section.dist;
    const rows = section.lines.map((l) => `<div class="line-row">
        <span class="num">over ${l.line}${unit}</span>
        ${l.overPct === null
          ? '<span class="mu">mẫu nhỏ</span>'
          : `<span class="line-bar"><span style="width:${l.overPct}%"></span></span>
             <span class="num line-pct">${l.overPct}%</span>`}
      </div>`).join('');

    return `<div class="pred-block">
      <h3>${esc(title)}</h3>
      <p class="desc">Trung vị <b>${d.median}${unit}</b> · nửa số trận nằm trong
         <b>${d.p25}–${d.p75}${unit}</b> · dải ${d.min}–${d.max}${unit} qua ${d.count} ván</p>
      ${rows}
    </div>`;
  }
}

/* ============================ Biến động ============================ */

function setupChanges() {
  $$('#changes-controls .pill').forEach((btn) => {
    btn.onclick = () => {
      $$('#changes-controls .pill').forEach((b) =>
        b.setAttribute('aria-pressed', String(b === btn)));
      loadChanges(Number(btn.dataset.days));
    };
  });

  loadChanges(1);
  loadSeries();
}

async function loadChanges(days) {
  const body = $('#changes-body');
  body.innerHTML = '<div class="skeleton" style="height:160px"></div>';

  try {
    const r = await getJson(`api/changes?days=${days}`);

    if (!r.changes || !r.changes.length) {
      body.innerHTML = `<div class="empty">${esc(r.note || 'Không có biến động nào vượt ngưỡng đáng chú ý.')}</div>`;
      return;
    }

    body.innerHTML = `<p class="desc" style="margin-bottom:var(--s-3)">
        So <b>${esc(r.baseline)}</b> với <b>${esc(r.latest)}</b> · ${r.changes.length} biến động</p>` +
      r.changes.map((c) => `<div class="change ${c.improved ? 'up' : 'down'}">
          <span class="change-arrow" aria-hidden="true">${c.improved ? '▲' : '▼'}</span>
          <div>
            <div class="change-text">${esc(c.narrative)}</div>
            <div class="change-meta num">${esc(c.label)} · ${c.before} → ${c.after}
              · mạnh gấp ${c.magnitude}× ngưỡng</div>
          </div>
        </div>`).join('');
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/changes</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

async function loadSeries() {
  const body = $('#series-body');
  try {
    const s = await getJson('api/series');

    if (!s.seriesCount) {
      body.innerHTML = '<div class="empty">Chưa nhận diện được series nào.</div>';
      return;
    }

    body.innerHTML = `<div class="bento">
      <article class="kpi p3">
        <div class="kpi-label">Thắng ván 1 → thắng series</div>
        <div class="kpi-value">${fmt(s.game1PredictsSeriesPct, 1, '%')}</div>
        <div class="kpi-note">qua ${s.decided} series có kết quả</div>
      </article>
      <article class="kpi p4">
        <div class="kpi-label">Số series</div>
        <div class="kpi-value">${s.seriesCount}</div>
        <div class="kpi-note">${s.gamesInSeries} ván nằm trong series</div>
      </article>
    </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/series</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

/* ============================ Chỉ số cá nhân ============================ */

function setupPlayerStats() {
  const sel = $('#player-team');
  sel.innerHTML = '<option value="">— Tất cả các đội —</option>' +
    state.teams.map((t) => `<option value="${esc(t.slug)}">${esc(t.name)}</option>`).join('');
  sel.onchange = () => loadPlayerStats(sel.value);
  loadPlayerStats('');
}

async function loadPlayerStats(team) {
  const body = $('#player-body');
  body.innerHTML = '<div class="skeleton" style="height:180px"></div>';

  try {
    const r = await getJson('api/player-stats' + (team ? `?team=${encodeURIComponent(team)}` : ''));

    if (!r.players.length) {
      body.innerHTML = '<div class="empty">Chưa đủ dữ liệu (cần tối thiểu 5 ván mỗi người).</div>';
      return;
    }

    body.innerHTML = `<div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Tuyển thủ</th><th scope="col">Ván</th><th scope="col">K</th>
        <th scope="col">D</th><th scope="col">A</th><th scope="col">GPM</th>
        <th scope="col">XPM</th><th scope="col">10′ đầu</th>
      </tr></thead>
      <tbody>${r.players.map((p) => `<tr>
        <td><span style="font-weight:600">${esc(p.nick)}</span></td>
        <td class="num">${p.games}</td>
        <td class="num">${fmt(p.kills, 2)}</td>
        <td class="num">${fmt(p.deaths, 2)}</td>
        <td class="num">${fmt(p.assists, 2)}</td>
        <td class="num">${fmt(p.gpm)}</td>
        <td class="num">${fmt(p.xpm)}</td>
        <td class="num${p.killsFirst10 === null ? ' na' : ''}"
            ${p.earlySample ? `title="${p.earlySample} ván có timeline"` : ''}>
          ${fmt(p.killsFirst10, 2)}</td>
      </tr>`).join('')}</tbody>
    </table></div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/player-stats</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

/* ============================ Hero thực chiến ============================ */

function setupHeroPool() {
  const sel = $('#hero-team');
  sel.innerHTML = '<option value="">— Toàn giải —</option>' +
    state.teams.map((t) => `<option value="${esc(t.slug)}">${esc(t.name)}</option>`).join('');
  sel.onchange = () => loadHeroPool(sel.value);
  loadHeroPool('');
}

async function loadHeroPool(team) {
  const body = $('#hero-pool-body');
  body.innerHTML = '<div class="skeleton" style="height:140px"></div>';

  try {
    const r = await getJson('api/heroes' + (team ? `?team=${encodeURIComponent(team)}` : ''));

    if (!r.heroes.length) {
      body.innerHTML = '<div class="empty">Chưa đủ dữ liệu hero (cần tối thiểu 3 ván mỗi hero).</div>';
      return;
    }

    // Tên hero lấy từ bảng tra cứu trong players.json (pl.h): { heroId: [tên, mã ảnh] }
    const lookup = state.heroLookup || {};

    body.innerHTML = '<div class="hero-strip">' + r.heroes.map((h) => {
      const meta = lookup[h.heroId];
      const name = meta ? meta[0] : `Hero ${h.heroId}`;
      const img = meta ? meta[1] : null;
      const tone = h.winrate >= 55 ? 'good' : h.winrate <= 45 ? 'bad' : '';
      return `<div class="hero ${tone}" title="${esc(name)} · ${h.games} ván · winrate ${h.winrate}%">
        ${img ? `<img src="${HERO_CDN}crops/${esc(img)}.png" alt="${esc(name)}" loading="lazy"
             onerror="this.closest('.hero').classList.add('noimg');this.remove()">` : ''}
        <span class="name">${esc(name)}</span>
        <span class="fallback">${esc(name)}</span>
        <span class="hero-badge num">${h.games}·${Math.round(h.winrate)}%</span>
      </div>`;
    }).join('') + '</div>';
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/heroes</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

/* ============================ Học từ pro ============================ */

/**
 * Thẻ ảnh hero, TỰ BIẾN MẤT nếu tải lỗi.
 *
 * Vì sao cần: một <img> lỗi hiện ra biểu tượng ảnh vỡ của trình duyệt, và ba mươi biểu tượng
 * ảnh vỡ xếp thành cột trông như trang bị hỏng — trong khi phần số liệu vẫn đúng nguyên. Không
 * có ảnh thì chỉ cần không có gì, đừng có một ô rỗng.
 */
function heroImg(url, w = 48, h = 27) {
  if (!url) return '';
  return `<img src="${esc(url)}" alt="" loading="lazy" width="${w}" height="${h}"
    onerror="this.remove()">`;
}

function itemImg(url) {
  return heroImg(url, 36, 27);
}

/** Giây -> "12:20". Đồ mua trước tiếng còi có thời gian âm, và đó là dữ liệu thật. */
function mmss(seconds) {
  if (seconds === null || seconds === undefined) return '—';
  if (seconds < 0) return 'trước còi';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

async function loadLearn() {
  loadDraft();
  loadItemHeroes();
  loadLanes();
  loadProPub();
  setupMe();
}

async function loadProPub() {
  const body = $('#propub-body');
  if (!body) return;
  body.innerHTML = '<div class="skeleton" style="height:200px"></div>';

  try {
    const d = await getJson('api/pro-pub');

    if (!d.heroes || !d.heroes.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa có dữ liệu.')}</div>`;
      return;
    }

    const max = Math.max(...d.heroes.map((h) => h.players));

    const rows = d.heroes.slice(0, 25).map((h) => `<tr>
      <td class="hero-cell">${heroImg(h.image)}<span>${esc(h.name)}</span></td>
      <td class="num"><b>${h.players}</b></td>
      <td><div class="minibar"><span style="width:${Math.round((h.players / max) * 100)}%"></span></div></td>
      <td class="num">${h.games}</td>
      <td class="num">${h.winrate}%</td>
      <td class="who">${(h.who || []).map((w) => esc(w)).join(', ')}</td>
    </tr>`).join('');

    body.innerHTML = `
      <div class="bento">
        <article class="kpi p2">
          <div class="kpi-label">Ván pub đã ghi</div>
          <div class="kpi-value">${d.totalGames}</div>
          <div class="kpi-note">${d.days} ngày gần nhất</div>
        </article>
        <article class="kpi p4">
          <div class="kpi-label">Tuyển thủ có dữ liệu</div>
          <div class="kpi-value">${d.totalPlayers}</div>
          <div class="kpi-note">${esc(d.schedule || '')}</div>
        </article>
      </div>

      <div class="table-scroll"><table>
        <caption class="sr-only">Hero được nhiều tuyển thủ chuyên nghiệp chơi trong pub gần đây</caption>
        <thead><tr>
          <th scope="col">Hero</th><th scope="col">Số người</th><th scope="col">Mức lan</th>
          <th scope="col">Ván</th><th scope="col">Thắng</th><th scope="col">Ai chơi</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.method || '')}<br><br>${esc(d.caveat || '')}</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/pro-pub</code>.<br><small>${esc(err.serverMessage || err.message)}</small></div>`;
  }
}

/* ============================ Tier list tính động ============================ */

const TL = { source: 'pro', position: null };

function setupTierList() {
  const src = $('#tl-source');
  const pos = $('#tl-positions');
  if (!src || src.dataset.ready) return;
  src.dataset.ready = '1';

  $$('button', src).forEach((btn) => {
    btn.onclick = () => {
      TL.source = btn.dataset.src;
      $$('button', src).forEach((b) => b.setAttribute('aria-pressed', String(b === btn)));
      loadTierList();
    };
  });

  // Nút vị trí: "Tất cả" cộng 5 vị trí. Tên lấy từ hằng số phía server qua lần tải đầu.
  const positions = [
    [null, 'Tất cả'], [1, 'Carry'], [2, 'Mid'], [3, 'Offlane'], [4, 'Hỗ trợ 4'], [5, 'Hỗ trợ 5'],
  ];

  pos.innerHTML = positions.map(([p, label], i) =>
    `<button class="pill" type="button" data-pos="${p ?? ''}" aria-pressed="${i === 0}">${label}</button>`
  ).join('');

  $$('button', pos).forEach((btn) => {
    btn.onclick = () => {
      TL.position = btn.dataset.pos ? Number(btn.dataset.pos) : null;
      $$('button', pos).forEach((b) => b.setAttribute('aria-pressed', String(b === btn)));
      loadTierList();
    };
  });

  loadTierList();
}

const TIER_META = {
  S: ['Thống trị', 'var(--neg)'],
  A: ['Rất mạnh', 'var(--pastel-1-ink)'],
  B: ['Ổn', 'var(--warn)'],
  C: ['Yếu thế', 'var(--muted)'],
};

async function loadTierList() {
  const body = $('#tl-body');
  body.innerHTML = '<div class="skeleton" style="height:280px"></div>';

  const q = new URLSearchParams({ source: TL.source });
  if (TL.position) q.set('position', TL.position);

  try {
    const d = await getJson(`api/tierlist?${q}`);

    if (!d.heroes || !d.heroes.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa đủ dữ liệu.')}</div>`;
      return;
    }

    // Cùng khung nhìn cho cả hai nguồn — đổi nguồn chỉ đổi GIÁ TRỊ, không đổi diện mạo.
    // Diện mạo đổi thì người đọc tưởng đang xem một thứ khác chứ không phải cùng thứ đo khác đi.
    const rows = ['S', 'A', 'B', 'C'].map((tier) => {
      const heroes = d.heroes.filter((h) => h.tier === tier);
      if (!heroes.length) return '';

      const [label, color] = TIER_META[tier];

      const cards = heroes.map((h) => {
        const bits = [];
        if (h.proContestRate !== null) bits.push(`giải ${h.proContestRate}%`);
        if (TL.source === 'combined' && h.highWinrate !== null) bits.push(`pub ${h.highWinrate}%`);
        if (h.positionGames) bits.push(`${h.positionGames} ván`);

        return `<div class="tl-hero" title="${esc(h.name + ' · ' + bits.join(' · '))}">
          ${heroImg(h.image, 58, 33)}
          <span class="tl-name">${esc(h.name)}</span>
          <span class="tl-num">${bits[0] || ''}</span>
        </div>`;
      }).join('');

      return `<div class="tier-row">
        <div class="tier-label" style="background:${color}">${tier}<small>${label}</small></div>
        <div class="hero-strip">${cards}</div>
      </div>`;
    }).join('');

    body.innerHTML = rows + `
      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div><b>${esc(d.patch)}</b> · ${d.draftsAnalysed} bàn draft.
        ${d.positionName ? `Vị trí <b>${esc(d.positionName)}</b> — ${esc(d.positionDesc || '')}.` : ''}
        <br><br>${esc(d.method || '')}
        <br><br>${esc(d.patchNote || '')}
        <br><br><small>${esc(d.limitation || '')}</small></div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/tierlist</code>.<br><small>${esc(err.serverMessage || err.message)}</small></div>`;
  }
}

async function loadLanes(role) {
  const body = $('#lane-body');
  body.innerHTML = '<div class="skeleton" style="height:200px"></div>';

  try {
    const d = await getJson('api/lanes' + (role ? `?role=${role}` : ''));

    if (!d.lanes || !d.lanes.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa có dữ liệu lane.')}</div>`;
      return;
    }

    // Nút lọc vị trí dựng từ chính các mốc mà API trả về, không hardcode
    const roles = $('#lane-roles');
    if (roles && !roles.dataset.ready && d.baselines) {
      roles.dataset.ready = '1';
      roles.innerHTML = `<button class="pill" type="button" data-role="" aria-pressed="true">Tất cả</button>` +
        d.baselines.map((b) =>
          `<button class="pill" type="button" data-role="${b.role}" aria-pressed="false">${esc(b.roleName)}</button>`
        ).join('');

      $$('#lane-roles button').forEach((btn) => {
        btn.onclick = () => {
          $$('#lane-roles button').forEach((b) =>
            b.setAttribute('aria-pressed', String(b === btn)));
          loadLanes(btn.dataset.role);
        };
      });
    }

    const baseline = (d.baselines || [])
      .map((b) => `${esc(b.roleName)} ${b.medianEfficiency}%`)
      .join(' · ');

    const rows = d.lanes.slice(0, 30).map((l) => {
      const tone = l.vsBaseline >= 5 ? 'cal-good' : l.vsBaseline <= -5 ? 'cal-bad' : '';
      return `<tr>
        <td class="hero-cell">
          ${heroImg(l.image)}
          <span>${esc(l.name)}</span>
        </td>
        <td>${esc(l.roleName)}</td>
        <td class="num">${l.medianEfficiency}%</td>
        <td class="num">${l.p25}% – ${l.p75}%</td>
        <td class="num ${tone}">${l.vsBaseline > 0 ? '+' : ''}${l.vsBaseline}</td>
        <td class="num">${l.winrate}%</td>
        <td class="num">${l.games}</td>
      </tr>`;
    }).join('');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Hiệu suất lane theo hero và vị trí</caption>
        <thead><tr>
          <th scope="col">Hero</th><th scope="col">Vị trí</th>
          <th scope="col">Hiệu suất</th><th scope="col">Khoảng thường gặp</th>
          <th scope="col">So mốc vị trí</th><th scope="col">Thắng ván</th><th scope="col">Ván</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>Mốc trung vị từng vị trí: ${baseline}.<br><br>${esc(d.caveat || '')}</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/lanes</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

function setupMe() {
  const input = $('#me-id');
  const btn = $('#me-go');
  if (!input || !btn || btn.dataset.ready) return;
  btn.dataset.ready = '1';

  const go = () => loadMe(input.value);
  btn.onclick = go;
  input.onkeydown = (e) => { if (e.key === 'Enter') go(); };

  // Nhớ id trong trình duyệt của chính người dùng, KHÔNG gửi đi đâu để lưu
  const saved = localStorage.getItem('ti2026-dota-id');
  if (saved) input.value = saved;
}

async function loadMe(rawId) {
  const body = $('#me-body');
  const id = (rawId || '').trim();

  if (!id) {
    body.innerHTML = '<div class="empty">Nhập Dota account ID hoặc Steam ID64 rồi bấm So sánh.</div>';
    return;
  }

  body.innerHTML = '<div class="skeleton" style="height:180px"></div>';

  try {
    const d = await getJson(`api/me?id=${encodeURIComponent(id)}`);
    localStorage.setItem('ti2026-dota-id', id);

    const rows = (d.heroes || []).map((h) => {
      const tone = h.verdict === 'pro cũng coi trọng' ? 'cal-good'
        : h.verdict === 'pro gần như đã bỏ' ? 'cal-bad' : '';
      return `<tr>
        <td class="hero-cell">
          ${heroImg(h.image)}
          <span>${esc(h.name)}</span>
        </td>
        <td class="num">${h.myGames}</td>
        <td class="num">${h.myWinrate}%</td>
        <td class="num">${h.proContestRate === null ? '—' : h.proContestRate + '%'}</td>
        <td class="${tone}">${esc(h.verdict)}</td>
      </tr>`;
    }).join('');

    body.innerHTML = `
      <div class="chip" style="margin-bottom:var(--s-4)">
        <b>${esc(d.name || ('ID ' + d.accountId))}</b> · so với ${d.proMatches} bàn draft ${esc(d.patch)}
      </div>

      <div class="table-scroll"><table>
        <caption class="sr-only">Hero bạn chơi nhiều nhất, đối chiếu với mức pro coi trọng</caption>
        <thead><tr>
          <th scope="col">Hero</th><th scope="col">Ván của bạn</th><th scope="col">Thắng</th>
          <th scope="col">Pro coi trọng</th><th scope="col">Nhận xét</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.caveat || '')}<br><br><small>${esc(d.privacy || '')}</small></div>
      </div>`;
  } catch (err) {
    // Hồ sơ riêng tư và hết suất tra là hai chuyện khác nhau — nói đúng chuyện nào đang xảy ra
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

async function loadDraft() {
  const body = $('#draft-body');
  body.innerHTML = '<div class="skeleton" style="height:220px"></div>';

  try {
    const d = await getJson('api/draft');

    if (!d.heroes || !d.heroes.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa có dữ liệu draft.')}</div>`;
      return;
    }

    $('#draft-desc').insertAdjacentHTML('beforeend',
      ` <b>${esc(d.patch)}</b> · ${d.matchesWithDraft} bàn draft.`);

    const max = Math.max(...d.heroes.map((h) => h.contestRate));

    const rows = d.heroes.slice(0, 30).map((h) => {
      const pct = Math.round((h.contestRate / max) * 100);
      // Cấm sớm là tín hiệu mạnh nhất trong bảng này, nên nó được tô màu riêng
      const feared = h.avgBanOrder !== null && h.avgBanOrder < 8 && h.bans >= 3;
      return `<tr>
        <td class="hero-cell">
          ${heroImg(h.image)}
          <span>${esc(h.name)}</span>
        </td>
        <td><div class="minibar"><span style="width:${pct}%"></span></div></td>
        <td class="num">${h.contestRate}%</td>
        <td class="num">${h.picks}</td>
        <td class="num">${h.bans}</td>
        <td class="num${feared ? ' cal-bad' : ''}">${h.avgBanOrder === null ? '—' : h.avgBanOrder}</td>
        <td class="num">${h.winrate === null ? '—' : h.winrate + '%'}</td>
      </tr>`;
    }).join('');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Tỷ lệ hero được cấm hoặc chọn ở bản hiện tại</caption>
        <thead><tr>
          <th scope="col">Hero</th><th scope="col">Được coi trọng</th><th scope="col">Tỷ lệ</th>
          <th scope="col">Chọn</th><th scope="col">Cấm</th>
          <th scope="col">Lượt cấm TB</th><th scope="col">Thắng khi chọn</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>
      <p class="desc" style="margin-top:var(--s-3)">Chỉ hiện hero xuất hiện từ ${d.minAppearances} bàn draft trở lên —
      dưới mức đó tỷ lệ nhảy quá mạnh theo từng ván. <b>Lượt cấm TB</b> càng nhỏ càng bị e dè;
      số đỏ là hero thường bị gạt ngay đầu bàn.</p>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/draft</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

async function loadItemHeroes() {
  const select = $('#item-hero');
  const body = $('#item-body');

  try {
    const d = await getJson('api/items');

    if (!d.heroes || !d.heroes.length) {
      body.innerHTML = '<div class="empty">Chưa có dữ liệu mua đồ. Cần nạp lại match detail.</div>';
      return;
    }

    select.innerHTML = d.heroes
      .map((h) => `<option value="${h.heroId}">${esc(h.name)}</option>`)
      .join('');

    select.onchange = () => loadItems(select.value);
    loadItems(select.value);
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/items</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

async function loadItems(heroId) {
  const body = $('#item-body');
  body.innerHTML = '<div class="skeleton" style="height:200px"></div>';

  try {
    const d = await getJson(`api/items?hero=${encodeURIComponent(heroId)}`);

    if (!d.items || !d.items.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa đủ mẫu cho hero này.')}</div>`;
      return;
    }

    const rows = d.items.map((it) => {
      const w = it.medianWinSeconds;
      const l = it.medianLossSeconds;

      // Lên sớm hơn trong ván thắng = mốc đó quan trọng. Chỉ tô khi lệch từ 45 giây trở lên;
      // dưới mức đó là dao động bình thường của trung vị trên mẫu nhỏ.
      let gap = '—';
      if (w !== null && l !== null) {
        const diff = l - w;
        const tone = diff >= 45 ? 'cal-good' : diff <= -45 ? 'cal-bad' : '';
        gap = `<span class="${tone}">${diff > 0 ? 'sớm hơn ' : diff < 0 ? 'muộn hơn ' : ''}${mmss(Math.abs(diff))}</span>`;
      }

      return `<tr>
        <td class="hero-cell">
          ${itemImg(it.image)}
          <span>${esc(it.name)}</span>
        </td>
        <td class="num">${it.cost === null || it.cost === undefined ? '—' : it.cost}</td>
        <td class="num"><b>${mmss(it.medianSeconds)}</b></td>
        <td class="num">${mmss(it.p25Seconds)} – ${mmss(it.p75Seconds)}</td>
        <td class="num">${mmss(w)}</td>
        <td class="num">${mmss(l)}</td>
        <td class="num">${gap}</td>
        <td class="num">${it.samples}</td>
      </tr>`;
    }).join('');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Mốc mua đồ của ${esc(d.heroName || '')} ở bản hiện tại</caption>
        <thead><tr>
          <th scope="col">Món</th><th scope="col">Giá</th>
          <th scope="col">Trung vị</th><th scope="col">Khoảng thường gặp</th>
          <th scope="col">Ván thắng</th><th scope="col">Ván thua</th>
          <th scope="col">Thắng lên sớm hơn</th><th scope="col">Mẫu</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.caveat || '')}<br><br><small>${esc(d.filterNote || '')}</small></div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được mốc lên đồ.<br><small>${esc(err.message)}</small></div>`;
  }
}

/* ============================ Tab & theme ============================ */

/** Tab nào đã nạp dữ liệu rồi, để không gọi lại mỗi lần người dùng bấm qua bấm lại. */
const loadedViews = new Set();

/**
 * Chia mỗi tab nhiều thẻ thành các mục con bấm được, thay vì bắt cuộn dài.
 *
 * Vì sao làm chung một chỗ cho mọi tab thay vì viết riêng: một tab được thêm thẻ thứ ba là
 * chuyện sẽ xảy ra, và nếu cơ chế nằm rải rác thì lần đó lại phải sửa từng nơi. Ở đây chỉ cần
 * gắn data-sec vào thẻ mới là nó tự có mục con.
 */
function setupSubTabs() {
  $$('.view').forEach((view) => {
    const sections = $$('[data-sec]', view);
    if (sections.length < 2) return;

    const nav = document.createElement('div');
    nav.className = 'subnav';
    nav.setAttribute('role', 'tablist');
    nav.setAttribute('aria-label', 'Mục trong trang');

    sections.forEach((sec, i) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'subpill';
      btn.textContent = sec.dataset.sec;
      btn.setAttribute('role', 'tab');
      btn.setAttribute('aria-selected', String(i === 0));

      btn.onclick = () => {
        $$('.subpill', nav).forEach((b) =>
          b.setAttribute('aria-selected', String(b === btn)));
        sections.forEach((s) => { s.hidden = s !== sec; });

        // Đưa về đầu mục: đổi mục xong mà vẫn ở giữa trang thì người dùng tưởng không có gì đổi
        view.scrollIntoView({ block: 'start', behavior: 'smooth' });
      };

      nav.appendChild(btn);
      sec.hidden = i !== 0;
    });

    view.prepend(nav);
  });
}

function setupTabs() {
  $$('nav button[role="tab"]').forEach((btn) => {
    btn.onclick = () => {
      $$('nav button[role="tab"]').forEach((b) =>
        b.setAttribute('aria-selected', String(b === btn)));
      $$('.view').forEach((v) => {
        const active = v.id === 'view-' + btn.dataset.view;
        v.classList.toggle('active', active);
        v.hidden = !active;
      });

      // Nạp muộn: hai truy vấn của tab này quét bảng mua đồ cả triệu hàng, không có lý gì
      // bắt mọi người mở trang phải chờ nó khi họ chỉ muốn xem bảng xếp hạng.
      const view = btn.dataset.view;
      if (view === 'tiers' && !loadedViews.has('tiers')) {
        loadedViews.add('tiers');
        setupTierList();
      }

      if (view === 'learn' && !loadedViews.has('learn')) {
        loadedViews.add('learn');
        loadLearn();
      }
    };
  });
}

function setupTheme() {
  const stored = localStorage.getItem('ti2026-theme');
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(stored || (prefersDark ? 'dark' : 'light'));

  $('#theme-toggle').onclick = () => {
    const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    localStorage.setItem('ti2026-theme', next);
  };
}

function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  const dark = theme === 'dark';
  $('#theme-toggle').setAttribute('aria-label',
    dark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối');
  $('#theme-icon').innerHTML = dark
    ? '<circle cx="12" cy="12" r="4"></circle><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"></path>'
    : '<path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>';
}

setupTheme();
setupTabs();
setupSubTabs();
boot();
