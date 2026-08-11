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
 *
 * null/undefined -> "—", KHÔNG phải "0". Đây là quy ước xuyên suốt: API trả null
 * cho chỉ số mà nguồn dữ liệu chưa cung cấp, và hiển thị 0 ở đó là bịa số liệu.
 *
 * Định dạng theo vi-VN: dấu chấm ngăn nghìn, dấu phẩy ngăn thập phân — "1.557", "25.111",
 * "58,3%". toFixed() không có dấu ngăn nghìn, nên "25111" phải đếm bằng mắt mới biết là bao
 * nhiêu, còn "13.733333333333333" thì không ai đọc nổi.
 */
const fmt = (v, digits = 0, suffix = '') =>
  (v === null || v === undefined || (typeof v === 'number' && !Number.isFinite(v)))
    ? '—'
    : Number(v).toLocaleString('vi-VN', {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
      }) + suffix;

/**
 * Số nguyên có dấu ngăn nghìn — dùng cho những chỗ nội suy thẳng vào template mà không
 * qua fmt(). Giữ nguyên "—" khi chưa biết.
 */
const n0 = (v) => fmt(v, 0);

/** Số có dấu, dùng cho mức thay đổi: "+42", "−1,3". */
const signed = (v, digits = 0) =>
  (v === null || v === undefined) ? '—' : (v > 0 ? '+' : '') + fmt(v, digits);

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
  loadSchedule();
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

/* ============================ Ô chọn đội có tìm kiếm ============================ */

/** Bỏ dấu tiếng Việt để gõ "wallachia" tìm được, và "Đông" khớp "dong". */
const deburr = (s) => (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '')
  .replace(/đ/gi, 'd').toLowerCase();

/**
 * Phủ một ô chọn có tìm kiếm lên trên <select> sẵn có.
 *
 * <select> gốc VẪN là nơi giữ giá trị: mọi đoạn mã cũ đọc `sel.value` hay gán `sel.onchange`
 * chạy y như trước, và mỗi lần chọn ở đây sẽ phát `change` để chúng chạy. Đổi hẳn sang thẻ
 * khác thì phải sửa mọi nơi đang dùng, và mỗi nơi bỏ sót là một tính năng chết lặng.
 *
 * Gọi SAU khi đã đổ option và gán value.
 */
function enhanceSelect(sel, placeholder = 'Gõ để tìm…') {
  if (!sel || sel.dataset.combo) return;
  sel.dataset.combo = '1';

  const wrap = document.createElement('div');
  wrap.className = 'combo';
  sel.parentNode.insertBefore(wrap, sel);
  wrap.appendChild(sel);
  sel.classList.add('combo-native');
  sel.setAttribute('aria-hidden', 'true');
  sel.tabIndex = -1;

  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'combo-btn';
  btn.setAttribute('aria-haspopup', 'listbox');
  btn.setAttribute('aria-expanded', 'false');

  const labelledBy = document.querySelector(`label[for="${sel.id}"]`);
  if (labelledBy) {
    if (!labelledBy.id) labelledBy.id = `${sel.id}-label`;
    btn.setAttribute('aria-labelledby', `${labelledBy.id} ${sel.id}-value`);
  }

  const pop = document.createElement('div');
  pop.className = 'combo-pop';
  pop.hidden = true;

  const search = document.createElement('input');
  search.type = 'text';
  search.className = 'combo-search';
  search.placeholder = placeholder;
  search.setAttribute('aria-label', placeholder);

  const list = document.createElement('div');
  list.className = 'combo-list';
  list.setAttribute('role', 'listbox');
  list.id = `${sel.id}-list`;

  pop.append(search, list);
  wrap.append(btn, pop);
  btn.setAttribute('aria-controls', list.id);

  const CARET = '<svg class="combo-caret" width="14" height="14" viewBox="0 0 24 24" fill="none" '
    + 'stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M6 9l6 6 6-6"/></svg>';

  let active = -1;
  let shown = [];

  const rows = () => [...sel.options].map((o) => ({
    value: o.value,
    label: o.text,
    team: state.teams.find((t) => t.slug === o.value) || null,
  }));

  function paintButton() {
    const r = rows().find((x) => x.value === sel.value);
    const logo = r && r.team ? teamLogo(r.team, 22) : '';
    btn.innerHTML =
      `${logo}<span class="combo-name" id="${sel.id}-value">${esc(r ? r.label : '—')}</span>${CARET}`;
  }

  function paintList() {
    const q = deburr(search.value.trim());
    shown = rows().filter((r) => !q || deburr(r.label).includes(q) || deburr(r.value).includes(q));

    if (shown.length === 0) {
      list.innerHTML = '<div class="combo-empty">Không có mục nào khớp.</div>';
      active = -1;
      return;
    }

    if (active >= shown.length) active = shown.length - 1;

    list.innerHTML = shown.map((r, i) => `
      <button type="button" class="combo-opt${i === active ? ' active' : ''}" role="option"
        aria-selected="${r.value === sel.value}" data-value="${esc(r.value)}">
        ${r.team ? teamLogo(r.team, 22) : ''}
        <span>${esc(r.label)}</span>
        ${r.team && r.team.region ? `<span class="region">${esc(r.team.region)}</span>` : ''}
      </button>`).join('');
  }

  function open() {
    wrap.dataset.open = '1';
    pop.hidden = false;
    btn.setAttribute('aria-expanded', 'true');
    search.value = '';

    // Vẽ lần đầu để `shown` có nội dung, rồi mới đặt được con trỏ vào đúng đội đang chọn.
    paintList();
    active = shown.findIndex((r) => r.value === sel.value);
    paintList();

    search.focus();
    list.querySelector('.combo-opt.active')?.scrollIntoView({ block: 'nearest' });
  }

  function close(focusBtn = true) {
    delete wrap.dataset.open;
    pop.hidden = true;
    btn.setAttribute('aria-expanded', 'false');
    if (focusBtn) btn.focus();
  }

  function choose(value) {
    if (value === sel.value) { close(); return; }
    sel.value = value;
    paintButton();
    close();
    // Phát change để onchange cũ chạy — đây là toàn bộ lý do <select> còn nằm lại.
    sel.dispatchEvent(new Event('change', { bubbles: true }));
  }

  btn.addEventListener('click', () => (pop.hidden ? open() : close()));

  search.addEventListener('input', () => { active = shown.length ? 0 : -1; paintList(); });

  search.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!shown.length) return;
      active = e.key === 'ArrowDown'
        ? (active + 1) % shown.length
        : (active - 1 + shown.length) % shown.length;
      paintList();
      list.querySelector('.combo-opt.active')?.scrollIntoView({ block: 'nearest' });
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (active >= 0 && shown[active]) choose(shown[active].value);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      close();
    }
  });

  list.addEventListener('click', (e) => {
    const opt = e.target.closest('.combo-opt');
    if (opt) choose(opt.dataset.value);
  });

  document.addEventListener('pointerdown', (e) => {
    if (!pop.hidden && !wrap.contains(e.target)) close(false);
  });

  paintButton();
}

/* ============================ Tổng quan ============================ */

const KPIS = [
  {
    label: 'Winrate cao nhất', cls: 'p2',
    pick: (a, b) => b.stats.winrate - a.stats.winrate,
    value: (t) => fmt(t.stats.winrate, 0, '%'),
    note: (t) => `${n0(t.stats.maps)} ván đã đấu`,
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
        ${thin ? `<span class="rank-thin" title="Ít ván nên chỉ số dao động mạnh">${n0(t.stats.maps)} ván</span>` : ''}
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
  enhanceSelect(a, 'Gõ để tìm đội…');
  enhanceSelect(b, 'Gõ để tìm đội…');
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

  // Hiện HẾT các ván trong một vùng cuộn, không cắt còn 10.
  //
  // Bản trước cắt `slice(0, 10)` nhưng nhãn vẫn ghi tổng số — nên trang nói "71 trận" rồi bày
  // ra 10 dòng, và không có chữ nào cho biết là đã cắt. Người đọc chỉ có thể kết luận là dữ
  // liệu bị thiếu.
  //
  // Và "71 trận" cũng sai đơn vị: 71 là số VÁN, còn số trận thật là 32. Một Bo3 đếm thành ba
  // trận thì mọi cặp đấu trông như đã gặp nhau gấp ba lần thực tế.
  const history = series && series.series && series.series.length
    ? `${verdictH2h(series, A, B)}
       <div style="margin-top:var(--s-5);padding-top:var(--s-4);border-top:1px solid var(--border)">
         <h3 style="font-size:13px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:var(--s-2)">
           Toàn bộ lịch sử · ${n0(series.seriesCount ?? 0)} trận · ${n0(series.games ?? series.n)} ván</h3>
         <p class="desc" style="margin:0 0 var(--s-3)">Kể cả ván của đội hình cũ${series.firstMet
            ? `, từ <b>${esc(series.firstMet)}</b> tới <b>${esc(series.lastMet)}</b>` : ''} —
            dòng mờ là ván KHÔNG tính vào nhận định ở trên.</p>
         <div class="table-scroll"><table>
           <caption class="sr-only">Từng ván đối đầu giữa hai đội, kèm số người của đội hình TI2026 có mặt</caption>
           <thead><tr>
             <th scope="col">Ngày</th>
             <th scope="col">Tỷ số<br><small style="font-weight:400;color:var(--muted)">${esc(A.name)} – ${esc(B.name)}</small></th>
             <th scope="col">Còn mấy người<br><small style="font-weight:400;color:var(--muted)">của đội hình TI2026</small></th>
             <th scope="col">Giải</th>
           </tr></thead>
           <tbody>${series.series.map((row) => h2hRow(row, A, B)).join('')}</tbody>
         </table></div>
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

/**
 * Nhận định đối đầu, lọc theo ĐỘI HÌNH chứ không theo ngày.
 *
 * Một cặp đấu chỉ là cùng một cặp đấu khi mười người trên sân vẫn là mười người đó. Falcons–Liquid
 * có 71 ván nhưng 51 ván trong đó Liquid chỉ còn 3/5 người của hôm nay — gộp cả vào rồi bảo "đối
 * đầu 71 trận" là đem thành tích của một đội khác ra để đoán trận sắp tới.
 *
 * Máy chọn tập ván đáng dùng và nói luôn cách biệt đó có nghĩa hay không; việc của người đọc
 * không phải là tự nhớ đội nào đổi người lúc nào.
 */
function verdictH2h(series, A, B) {
  const v = series.verdict;
  if (!v || !v.text) return '';

  const cls = v.basis === 'khong-du' ? 'none' : v.decisive ? '' : 'thin';

  const lineup = (T) => {
    const l = series.lineup && series.lineup[T.slug];
    return l
      ? `<div><b>${esc(T.name)}</b> đá cùng nhau từ <b>${esc(l.since)}</b> · ${l.games} ván</div>`
      : `<div><b>${esc(T.name)}</b> chưa từng ra sân đủ 5 người của đội hình TI2026</div>`;
  };

  return `<div class="h2h-verdict ${cls}">
    <div style="font-size:11.5px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:4px">
      Hệ thống nhận định</div>
    ${esc(v.text)}
    <div class="h2h-lineups">${lineup(A)}${lineup(B)}</div>
  </div>`;
}

/**
 * Một dòng lịch sử. Tỷ số xoay theo ĐÚNG hai đội đang chọn ở đầu trang.
 *
 * Bảng cũ in thẳng radiantScore – direScore mà không nói bên nào là radiant, nên "12 – 8" không
 * cho biết ai được 12 — bày ra một con số không đọc được.
 */
function h2hRow(row, A, B) {
  const [date, league, radScore, direScore, radSlug, radKept, direKept, winner] = row;

  // Payload cũ chưa có 4 field sau: vẫn hiện được ngày/giải, chỉ bỏ phần đội hình.
  const known = radSlug !== undefined && radKept !== undefined && direKept !== undefined;
  const aIsRad = radSlug === A.slug;

  const sa = !known || aIsRad ? radScore : direScore;
  const sb = !known || aIsRad ? direScore : radScore;
  const ka = aIsRad ? radKept : direKept;
  const kb = aIsRad ? direKept : radKept;

  const tone = (k) => (k >= 5 ? 'full' : k >= 3 ? 'swap' : 'gone');
  const off = known && Math.min(ka, kb) < 5;

  const score = known && winner
    ? `${winner === A.slug ? `<b>${esc(sa)}</b>` : esc(sa)} – ${winner === B.slug ? `<b>${esc(sb)}</b>` : esc(sb)}`
    : `<b>${esc(sa)} – ${esc(sb)}</b>`;

  return `<tr class="${off ? 'off-lineup' : ''}">
    <td>${esc(date)}</td>
    <td class="num">${score}</td>
    <td class="num">${known
      ? `<span class="kept ${tone(ka)}">${ka}</span><span style="color:var(--muted)">/</span><span class="kept ${tone(kb)}">${kb}</span>`
      : '—'}</td>
    <td>${esc(league)}</td>
  </tr>`;
}

/* ============================ Lịch thi đấu ============================ */

const STATUS_LABEL = {
  'sap-toi': 'Sắp tới',
  'dang-dien-ra': 'Đang diễn ra',
  'da-xong': 'Đã xong',
  'cho-ket-qua': 'Chờ kết quả',
  'chua-xep-gio': 'Chưa xếp giờ',
};

/** Ngày theo múi giờ MÁY NGƯỜI XEM — trận 6h sáng UTC là chiều hôm trước ở nhiều nơi. */
const dayKey = (iso) => new Date(iso).toLocaleDateString('vi-VN', {
  weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric',
});

const clockOf = (iso) => new Date(iso).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });

async function loadSchedule() {
  const body = $('#schedule-body');
  if (!body) return;
  body.innerHTML = '<div class="skeleton" style="height:220px"></div>';

  try {
    renderSchedule(await getJson('api/schedule'));
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/schedule</code>.<br>
      <small>${esc(err.message)}</small></div>`;
  }
}

/**
 * Ô một đội trong bảng đấu.
 *
 * Nút chưa biết đội nào vào thì KHÔNG để trống: nói nó nhận đội thắng từ nút nào. Đó là thông
 * tin thật của một bảng loại trực tiếp, và một ô trống trơn thì không phân biệt được với lỗi.
 */
function scheduleSide(team, fromNode, right) {
  const inner = team
    ? `${teamLogo({ name: team.name, logo: team.logo }, 26)}<span>${esc(team.name)}</span>`
    : `<span class="sc-tbd">${fromNode ? `Đội thắng nút ${n0(fromNode)}` : 'Chưa xác định'}</span>`;
  return `<div class="sc-side${right ? ' right' : ''}">${inner}</div>`;
}

function renderSchedule(d) {
  const body = $('#schedule-body');
  if (!body) return;

  if (!d.ready) {
    body.innerHTML = `<div class="note">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div>${esc(d.note || '')}</div></div>
      <p class="desc">${esc(d.source || '')}</p>`;
    return;
  }

  const row = (s) => {
    const decided = s.status === 'da-xong' || s.status === 'dang-dien-ra';
    const mid = decided
      ? `<span class="sc-score num"><b>${n0(s.wins1)}</b>–<b>${n0(s.wins2)}</b></span>`
      : `<span class="sc-time">${s.scheduledAt ? esc(clockOf(s.scheduledAt)) : '—'}</span>`;

    return `<div class="sc-row status-${esc(s.status)}">
      ${scheduleSide(s.team1, s.from1, false)}
      <div class="sc-mid">
        ${mid}
        <span class="sc-meta">${esc(s.name || STATUS_LABEL[s.status] || s.status)}</span>
      </div>
      ${scheduleSide(s.team2, s.from2, true)}
    </div>`;
  };

  // Trong một vòng, gom theo NGÀY khi Valve đã xếp giờ. Chưa xếp thì gộp thành một khối cuối
  // — xếp chúng vào một ngày bịa ra thì con số ngày trên trang là con số sai.
  const stage = (st) => {
    const timed = st.series.filter((s) => s.scheduledAt);
    const untimed = st.series.filter((s) => !s.scheduledAt);

    const byDay = new Map();
    for (const s of timed) {
      const k = dayKey(s.scheduledAt);
      if (!byDay.has(k)) byDay.set(k, []);
      byDay.get(k).push(s);
    }

    const days = [...byDay.entries()].map(([day, list]) =>
      `<h4 class="sc-day">${esc(day)}</h4><div class="sc-list">${list.map(row).join('')}</div>`).join('');

    const rest = untimed.length
      ? `<h4 class="sc-day">Chưa xếp giờ · ${n0(untimed.length)} loạt</h4>
         <div class="sc-list">${untimed.map(row).join('')}</div>`
      : '';

    return `<section class="sc-stage">
      <header>
        <h3>${esc(st.name)}</h3>
        <span class="sc-progress">${n0(st.done)}/${n0(st.total)} loạt đã xong</span>
      </header>
      ${days}${rest}
    </section>`;
  };

  body.innerHTML = `
    <div class="sc-summary">
      <span><b>${n0(d.totalSeries)}</b> loạt trong bảng đấu</span>
      <span><b>${n0(d.scheduledSeries)}</b> đã có giờ</span>
      <span><b>${n0(d.completedSeries)}</b> đã xong</span>
    </div>
    ${d.note ? `<div class="note">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div>${esc(d.note)}</div></div>` : ''}
    ${d.stages.map(stage).join('')}
    <p class="desc" style="margin-top:var(--s-4)">${esc(d.source || '')}</p>`;
}


/* ============================ Hồ sơ cá nhân ============================ */

let profileReady = false;

async function setupProfile() {
  const sel = $('#profile-who');
  if (!sel) return;

  if (!profileReady) {
    try {
      const d = await getJson('api/profile/people');
      // Chỉ tên. note là ghi chú NỘI BỘ cho người sửa tracked-players.json ("chu trang",
      // "hoc tro"), không phải nhãn cho người xem — dán nó vào đây làm ô chọn vừa dài vừa lộ
      // một chuỗi không dấu giữa một trang tiếng Việt có dấu.
      sel.innerHTML = (d.people || []).map((p) =>
        `<option value="${esc(String(p.accountId))}">${esc(p.name)}</option>`).join('');
      sel.onchange = () => loadProfile(sel.value);
      enhanceSelect(sel, 'Gõ để tìm người…');
      profileReady = true;
    } catch (err) {
      $('#profile-head').innerHTML =
        `<div class="error">Không tải được danh sách người theo dõi.<br><small>${esc(err.message)}</small></div>`;
      return;
    }
  }

  loadProfile(sel.value);
}

async function loadProfile(accountId) {
  // Khung xương đặt ở mục ĐANG MỞ, không phải luôn ở mục đầu: người dùng có thể đang đứng ở
  // "Đồng đội" rồi đổi người, và nếu chỉ mục Tổng quan báo đang tải thì họ nhìn vào một bảng
  // cũ của người cũ mà tưởng đó là dữ liệu mới.
  ['overview', 'roles', 'heroes', 'mates', 'months', 'insights'].forEach((k) => {
    const el = $('#profile-' + k);
    if (el) el.innerHTML = '<div class="skeleton" style="height:220px"></div>';
  });

  try {
    renderProfile(await getJson('api/profile' + (accountId ? `?player=${encodeURIComponent(accountId)}` : '')));
  } catch (err) {
    $('#profile-overview').innerHTML = `<div class="error">Không tải được <code>api/profile</code>.<br>
      <small>${esc(err.message)}</small></div>`;
  }
}

function renderProfile(d) {
  // Mỗi mục con một khung riêng. Trang này từng là MỘT khối duy nhất dài hàng nghìn pixel — và
  // đó là lỗi bỏ qua cơ chế đã có sẵn: mọi tab khác đều chia mục bằng data-sec, chỉ trang này
  // tự dựng riêng một trang cuộn dài.
  const pane = (id) => $('#profile-' + id);

  if (!d.ready) {
    pane('head').innerHTML =
      `<div class="empty">${esc(d.note || d.syncNote || 'Chưa có dữ liệu.')}</div>`;
    ['overview', 'roles', 'heroes', 'mates', 'months', 'insights']
      .forEach((k) => { pane(k).innerHTML = ''; });
    return;
  }

  const m = d.me;
  const wr = m.lifetimeWinrate;

  pane('head').innerHTML = `
    <div class="pf-head">
      ${m.avatar
        ? `<img class="pf-avatar" src="${esc(m.avatar)}" alt="" loading="lazy">`
        : `<span class="pf-avatar logo-fallback">${esc((m.name || '?').charAt(0))}</span>`}
      <div class="pf-id">
        <strong>${esc(m.name)}</strong>
        <div class="pf-sub">${esc(m.persona || '')}</div>
      </div>
      <div class="pf-kpis">
        <div><span class="pf-num">${esc(m.rank || '—')}</span><small>hạng</small></div>
        <div><span class="pf-num">${fmt(wr, 1, '%')}</span><small>thắng cả đời</small></div>
        <div><span class="pf-num">${n0(m.wins + m.losses)}</span><small>ván cả đời</small></div>
        <div><span class="pf-num">${n0(m.storedGames)}</span><small>ván đã lưu</small></div>
      </div>
    </div>
    <p class="desc" style="margin-bottom:0">Lịch sử đã lưu:
      ${m.from ? esc(m.from.slice(0, 10)) : '—'} → ${m.to ? esc(m.to.slice(0, 10)) : '—'}.
      Mọi mục bên dưới tính trên khoảng này, không phải trên toàn bộ
      ${n0(m.wins + m.losses)} ván cả đời.</p>`;

  pane('overview').innerHTML = headlines(d) + skillBlock(d);
  pane('roles').innerHTML = roleBlock(d) + deathBlock(d) + eraBlock(d);
  pane('heroes').innerHTML = heroBlock(d);
  pane('mates').innerHTML = matesBlock(d);
  pane('months').innerHTML = monthBlock(d);

  pane('insights').innerHTML = ((d.insights || []).length
    ? `<h3 style="margin-top:0">Hệ thống đọc được gì</h3>
       <div class="insight-card"><ul>${d.insights.map((i) => `
         <li class="tone-${esc(i.tone)}">
           <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"
                stroke-linecap="round" stroke-linejoin="round">${TONE_ICON[i.tone] || TONE_ICON.flat}</svg>
           <span>${esc(i.text)}</span>
         </li>`).join('')}</ul></div>`
    : '<div class="empty">Chưa đủ dữ liệu để nhận định.</div>')
    + more('Cách tính và giới hạn của từng con số', 0,
        `<p class="desc" style="margin:0 0 var(--s-3)">Chỉ những vị trí có nhãn THẬT từ replay mới
           tách riêng được bằng các nút ở mục Tổng quan. Vị trí suy đoán thì không tách — nó
           không phân biệt được mid với offlane. Dải mờ hẹp nghĩa là bạn ổn định, dải rộng nghĩa
           là thất thường: hai người cùng trung vị 60 có thể là một người luôn quanh 60 và một
           người khi 90 khi 20.</p>
         <p class="desc" style="margin:0">${esc(d.method || '')}</p>`);

  wireRoleChips(d);
}

/* -------------------------------- Vai trò -------------------------------- */

function roleBlock(d) {
  const roles = d.roles || [];
  if (!roles.length) return '<div class="empty">Chưa đủ ván để tách theo vai trò.</div>';

  return `<h3 style="margin-top:0">Vai trò đã chơi</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Vị trí chính xác chỉ đến từ <b>nhãn replay</b>.
      Ván chưa parse thì chỉ nói được core hay hỗ trợ, và dòng đó ghi rõ là suy luận — mức farm
      KHÔNG dùng để đoán lane, vì đo trên chính tài khoản này thì last hit ở safelane, mid và
      offlane lần lượt là 299 / 345 / 282, gần như bằng nhau.</p>
    <div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Vai trò</th><th scope="col">Ván</th>
        <th scope="col">Thắng</th><th scope="col">Nguồn</th>
      </tr></thead>
      <tbody>${roles.map((r) => `<tr>
        <td>${esc(r.label)}</td>
        <td class="num">${n0(r.games)}</td>
        <td class="num ${r.winrate >= 55 ? 'cal-good' : r.winrate <= 45 ? 'cal-bad' : ''}">${fmt(r.winrate, 1, '%')}</td>
        <td class="mu">${r.exact ? 'nhãn replay' : 'suy từ thứ hạng tài sản'}</td>
      </tr>`).join('')}</tbody>
    </table></div>`;
}

/* --------------------- Cái chết có đổi được gì không --------------------- */

const DEATH_VERDICT = {
  'ho-tro': ['good', 'Có: ván bạn chết nhiều là ván đồng đội farm tốt hơn — ở CẢ ván thắng lẫn ván thua.'],
  'phan-bac': ['warn', 'Không: ván bạn chết nhiều là ván đồng đội farm KÉM hơn, ở cả hai loại kết quả.'],
  'khong-ro': ['flat', 'Chưa kết luận được: ván thắng và ván thua nói ngược nhau, nên đây là hai hiệu ứng khác nhau chứ không phải một quy luật.'],
  'khong-du-du-lieu': ['flat', 'Chưa đủ ván ở cả hai nhóm thắng và thua để so.'],
};

/**
 * Trả lời "cái chết của tôi có tạo ra khoảng trống cho đồng đội không" bằng kinh tế đồng đội.
 *
 * Không dùng chỉ số hỗ trợ: nó chỉ ghi nhận việc CÓ MẶT lúc hạ gục, mà người đã chết thì không
 * thể có mặt ở pha hạ gục sau đó.
 */
function deathBlock(d) {
  const de = d.deathEffect;
  if (!de || !de.splits || !de.splits.length) return '';

  const [tone, verdict] = DEATH_VERDICT[de.verdict] || DEATH_VERDICT['khong-ro'];

  return `<h3 style="margin-top:var(--s-5)">Cái chết của bạn có đổi được gì không</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Câu này KHÔNG trả lời được bằng chỉ số hỗ trợ —
      hỗ trợ chỉ ghi nhận việc có mặt lúc hạ gục, mà người đã chết thì không thể có mặt ở pha hạ
      gục sau đó. Thứ đo được là <b>mức farm của 4 đồng đội trong chính ván đó</b>: nếu lối chơi
      hi sinh có hiệu quả thì ván bạn chết nhiều phải là ván đồng đội giàu hơn thường lệ.
      So trong <b>cùng một kết quả trận</b>, vì thắng thua kéo mọi chỉ số đi theo.</p>
    <div class="insight-card"><ul><li class="tone-${esc(tone)}">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"
           stroke-linecap="round" stroke-linejoin="round">${TONE_ICON[tone] || TONE_ICON.flat}</svg>
      <span>${esc(verdict)}</span>
    </li></ul></div>
    <div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Nhóm ván</th><th scope="col">Ván</th>
        <th scope="col">Bạn chết NHIỀU nhất</th><th scope="col">Bạn chết ÍT nhất</th>
        <th scope="col">Chênh</th><th scope="col">p</th>
      </tr></thead>
      <tbody>${de.splits.map((s) => `<tr>
        <td>${esc(s.outcome)}</td>
        <td class="num mu">${n0(s.games)}</td>
        <td class="num">${s.highDeathMatesFarm}</td>
        <td class="num">${s.lowDeathMatesFarm}</td>
        <td class="num ${s.matesFarmGap > 0 ? 'cal-good' : s.matesFarmGap < 0 ? 'cal-bad' : ''}">${signed(s.matesFarmGap)}</td>
        <td class="num mu">${s.pValue < 0.001 ? '&lt;0,001' : fmt(s.pValue, 3)}</td>
      </tr>`).join('')}</tbody>
    </table></div>
    <p class="desc" style="margin-top:var(--s-3)">Hai cột giữa là <b>phân vị farm của đồng đội</b>,
      không phải của bạn. Đây là <b>tương quan trong cùng một ván, không phải nhân quả</b>: ván
      đồng đội farm tốt cũng có thể là ván bạn dám lao vào hơn vì biết đội đang mạnh. Dữ liệu
      không phân biệt được hai chiều đó.</p>`;
}

/* --------------------------------- Hero --------------------------------- */

/**
 * MỘT bảng hero duy nhất, không phải hai.
 *
 * Bản trước có "Hero pool" và "Hero pool so với meta" là hai bảng rời, cùng liệt kê đúng 125
 * hero đó — người đọc phải cuộn hết bảng thứ nhất rồi tự ghép với bảng thứ hai để trả lời một
 * câu hỏi duy nhất: hero này mình chơi thế nào so với người khác.
 */
function heroBlock(d) {
  const heroes = (d.heroes || []).filter((h) => h.games >= 5);
  if (!heroes.length) return '<div class="empty">Chưa đủ ván trên hero nào.</div>';

  const meta = new Map((d.meta || []).map((h) => [h.heroId, h]));
  const untouched = d.metaUntouched || [];
  const rows = heroes.map((h) => ({ ...h, m: meta.get(h.heroId) }));

  return `<h3 style="margin-top:0">Hero pool</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Cột <b>Mức chung</b> là tỷ lệ thắng của chính
      hero đó ở bậc rank cao, và <b>Chênh</b> là bạn hơn kém mức ấy bao nhiêu. Đây mới là cột
      đáng đọc: hero mạnh sẵn thì ai chơi cũng thắng, nên 53% với một hero có mức chung 53% là
      đúng bằng mọi người, còn 50% với hero có mức chung 44% là hơn hẳn. Dấu ★ là chênh lệch đã
      vượt ngưỡng nhiễu sau khi tính tới cả pool.</p>
    ${noStarNote(d, d.meta || [])}
    <div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Hero</th><th scope="col">Ván</th><th scope="col">Thắng</th>
        <th scope="col">Mức chung</th><th scope="col">Chênh</th>
        <th scope="col">GPM</th><th scope="col">Pro</th>
        <th scope="col">LH/phút</th><th scope="col">KDA</th>
      </tr></thead>
      <tbody>${rows.map((h) => `<tr>
        <td>${esc(h.name)}${h.m && h.m.notable ? ' <b title="Chênh lệch đã vượt ngưỡng nhiễu">★</b>' : ''}</td>
        <td class="num">${n0(h.games)}</td>
        <td class="num">${fmt(h.winrate, 1, '%')}</td>
        <td class="num mu">${h.m ? fmt(h.m.metaWinrate, 1, '%') : '—'}</td>
        <td class="num ${h.m && h.m.edge > 0 ? 'cal-good' : h.m && h.m.edge < 0 ? 'cal-bad' : ''}">${h.m ? signed(h.m.edge, 1) : '—'}</td>
        <td class="num">${fmt(h.gpm, 0)}</td>
        <td class="num mu">${h.proGpm === null ? '—' : fmt(h.proGpm, 0)}</td>
        <td class="num">${fmt(h.lastHitsPerMin, 1)}</td>
        <td class="num">${fmt(h.kda, 2)}</td>
      </tr>`).join('')}</tbody>
    </table></div>
    ${untouched.length ? `<p class="desc" style="margin-top:var(--s-3)">Đang mạnh trong meta mà
      bạn gần như chưa chơi: ${untouched.map((u) =>
        `<b>${esc(u.name)}</b> (${fmt(u.metaWinrate, 1, '%')})`).join(', ')}.
      Cố ý không gọi đây là "nên học" — một hero mạnh ở mức chung chưa chắc hợp với vị trí hay
      lối chơi của bạn, và trang không có cách nào biết điều đó.</p>` : ''}`;
}

/* ---------------------------- Theo thời gian ---------------------------- */

function monthBlock(d) {
  const months = d.months || [];
  if (!months.length) return '<div class="empty">Chưa có tháng nào đủ dữ liệu.</div>';

  const maxG = Math.max(...months.map((x) => x.games), 1);
  const mt = d.monthTrend;

  return `<h3 style="margin-top:0">Diễn biến theo tháng</h3>
    ${mt && mt.text ? `<p class="desc" style="margin:0 0 var(--s-3)"><b>Xu hướng:</b> ${esc(mt.text)}</p>` : ''}
    <div class="pf-months">${months.map((x) => `
      <div class="pf-month${x.thin ? ' thin' : ''}">
        <div class="pf-bar" style="height:${Math.max((x.games / maxG) * 100, 6)}%"
             title="${n0(x.games)} ván"></div>
        <div class="pf-wr ${x.winrate >= 50 ? 'cal-good' : 'cal-bad'}">${fmt(x.winrate, 0, '%')}</div>
        <div class="pf-lbl">${esc(x.month.slice(5))}/${esc(x.month.slice(2, 4))}</div>
        <div class="pf-gpm" title="${x.pctLastHits === null ? 'chưa đủ ván có phân vị'
          : `phân vị ăn lính, ${n0(x.ratedGames)} ván`}">${x.pctLastHits === null ? '·' : x.pctLastHits}</div>
      </div>`).join('')}</div>
    <p class="desc">Cột là số ván, số trên là tỷ lệ thắng, số dưới là <b>phân vị ăn lính</b> của
      tháng đó. Dùng phân vị chứ không dùng GPM trung bình: GPM phụ thuộc nặng vào việc tháng đó
      hay chơi hero nào — một tháng chơi nhiều hỗ trợ sẽ tụt GPM mà chẳng liên quan gì tới kỹ
      năng. Dấu · là tháng chưa đủ 5 ván có phân vị. Tháng mờ là tháng dưới 10 ván.</p>`;
}


function more(title, count, inner) {
  return `<details class="pf-more">
    <summary><span>${esc(title)}</span>${count ? `<b>${n0(count)}</b>` : ''}</summary>
    <div class="pf-more-body">${inner}</div>
  </details>`;
}

/* ------------------------------ Thẻ kết luận ------------------------------ */

/**
 * Bốn kết luận lớn nhất, đặt ngay đầu trang.
 *
 * CHỌN THEO TÍN HIỆU, KHÔNG THEO CHỦ ĐỀ. Dữ liệu thật đã chỉ rõ phần nào nói được và phần nào
 * không: phân vị kỹ năng dựng trên 5.877 ván nên rất chắc, còn tỷ lệ thắng theo từng hero thì
 * cần khoảng 140 ván MỖI hero mới tách được khỏi nhiễu — mà trung bình chỉ có 47. Nên bốn thẻ
 * này chỉ lấy từ phân vị và từ vai trò có nhãn thật; bảng hero xuống dưới, gập lại.
 *
 * Thẻ nào không đủ dữ liệu thì KHÔNG hiện, thay vì hiện một ô trống hay một con số bịa — người
 * mới được theo dõi sẽ chỉ có một hai thẻ, và thế là đúng.
 */
function headlines(d) {
  const all = d.components || [];
  if (!all.length) {
    return `<div class="hl-empty">Chưa có ván nào lấy được phân vị. Vòng nạp kế tiếp sẽ lấy
      bối cảnh cả 10 người của từng ván — sau đó trang này mới chấm được từng mặt kỹ năng.</div>`;
  }

  // Bỏ những cột mỏng khỏi cuộc đua "mạnh nhất / yếu nhất". Hồi máu chỉ có ở 989/5.877 ván (vì
  // phân vị của một ván hồi 0 máu là vô nghĩa nên đã bị loại), và để nó tranh ngôi mặt mạnh
  // nhất là để một cột dựng trên 1/6 dữ liệu nói thay cho cả hồ sơ.
  const maxGames = Math.max(...all.map((c) => c.games));
  const solid = all.filter((c) => c.games >= maxGames * 0.5);
  const pool = solid.length >= 3 ? solid : all;

  const cards = [];

  const best = [...pool].sort((a, b) => b.median - a.median)[0];
  if (best && best.median >= 55) {
    cards.push(card('good', 'Mặt mạnh nhất', best.median, best.label,
      `Hơn ${best.median}% người chơi cùng hero, qua ${n0(best.games)} ván.`));
  }

  const worst = [...pool].sort((a, b) => a.median - b.median)[0];
  if (worst && worst.median <= 45 && worst.key !== (best || {}).key) {
    const roles = (d.componentsByRole || [])
      .map((r) => (r.components.find((c) => c.key === worst.key) || {}).median)
      .filter((v) => v !== undefined);

    const everywhere = roles.length >= 3 && roles.every((v) => v <= 50);

    // Chữ phải TƯƠNG XỨNG với cỡ cách biệt. "Đáng sửa nhất" là một lời khuyên, và ở phân vị 42
    // — tức chỉ kém trung bình 8 điểm — nó nặng hơn thứ dữ liệu đỡ được. Dưới 35 mới là cách
    // biệt rõ; khoảng 35–45 chỉ nói được đây là cột thấp nhất trong mười cột của chính người đó.
    const sharp = worst.median <= 35;

    cards.push(card('bad', sharp ? 'Đáng sửa nhất' : 'Thấp nhất', worst.median, worst.label,
      everywhere
        ? `Thấp ở CẢ ${roles.length} vị trí — đây là thói quen đi theo bạn, không phải chuyện chọn sai vai trò.`
        : sharp
          ? `Rõ rệt dưới mức trung bình của người chơi cùng hero, qua ${n0(worst.games)} ván.`
          : `Thấp nhất trong ${n0(all.length)} mặt của bạn, kém trung bình ${50 - worst.median} điểm — đáng để ý, chưa tới mức báo động.`));
  }

  const moved = all
    .filter((c) => c.recent !== null && c.recent !== undefined)
    .map((c) => ({ c, gap: c.recent - c.median }))
    .sort((a, b) => Math.abs(b.gap) - Math.abs(a.gap))[0];

  if (moved && Math.abs(moved.gap) >= 10) {
    const up = moved.gap > 0;
    cards.push(card(up ? 'good' : 'bad', up ? 'Đang lên' : 'Đang xuống',
      `${up ? '+' : '−'}${Math.abs(moved.gap)}`, moved.c.label,
      `50 ván gần nhất ở phân vị ${moved.c.recent}, so với ${moved.c.median} tính trên cả lịch sử.`));
  }

  const role = (d.roles || []).filter((r) => r.exact).sort((a, b) => b.games - a.games)[0];
  if (role) {
    cards.push(card('flat', 'Vị trí chính', role.games, role.label,
      `Ván có nhãn vị trí THẬT đọc từ replay, thắng ${fmt(role.winrate, 1, '%')}.`));
  }

  if (!cards.length) return '';

  return `<div class="hl-grid">${cards.join('')}</div>`;
}

function card(tone, kicker, big, title, note) {
  return `<div class="hl hl-${tone}">
    <div class="hl-kicker">${esc(kicker)}</div>
    <div class="hl-big">${typeof big === 'number' ? n0(big) : esc(String(big))}</div>
    <div class="hl-title">${esc(title)}</div>
    <div class="hl-note">${esc(note)}</div>
  </div>`;
}

/* ------------------------------ Biểu đồ ra-đa ------------------------------ */

/**
 * Bảy trục cố định, theo đúng thứ tự này ở MỌI người.
 *
 * Cố định để hình dạng so được giữa hai người: nếu thứ tự trục đổi theo dữ liệu thì hai đa giác
 * khác hình có thể chỉ vì trục xếp khác, chứ không phải vì người chơi khác nhau.
 *
 * Bỏ "chối lính" (gần trùng với ăn lính, hai trục cạnh nhau đo cùng một việc sẽ kéo dài hình về
 * một phía một cách giả tạo) và "hồi máu" (chỉ có ở một phần nhỏ số ván, xem quy tắc raw = 0).
 */
const RADAR_AXES = [
  ['farm-gpm', 'Kiếm vàng'],
  ['farm-lh', 'Ăn lính'],
  ['xpm', 'Lên cấp'],
  ['dmg', 'Sát thương'],
  ['tower', 'Đẩy trụ'],
  ['assists', 'Hỗ trợ'],
  ['deaths', 'Giữ mạng'],
];

/**
 * Ra-đa bảy góc: vươn ra ở mặt mạnh, thụt vào ở mặt yếu.
 *
 * VÌ SAO DẠNG NÀY DÙNG ĐƯỢC Ở ĐÂY, trong khi ra-đa thường bị chê. Điều khiến nó sai trong đa số
 * trường hợp là mỗi trục một đơn vị khác nhau, nên diện tích hình chẳng có nghĩa gì. Ở đây cả
 * bảy trục đều là CÙNG một thang: phân vị 0→100 so với người chơi cùng hero. Cùng thang, cùng
 * chiều (cao luôn là tốt, số chết đã đảo), nên hình dạng đọc được thật.
 *
 * VÒNG 50 ĐƯỢC VẼ ĐẬM. Không có nó thì một đa giác trông "khá to" mà thực ra chỉ quanh mức
 * trung bình — vòng đó là ranh giới giữa hơn người và kém người, và nó phải nhìn thấy được.
 *
 * TRỤC KHÔNG BAO GIỜ CẮT GỐC. Luôn 0→100 đủ vòng, kể cả khi mọi giá trị nằm trong 40–70: co
 * thang cho "vừa dữ liệu" sẽ thổi một chênh lệch 8 điểm thành một hình dạng kịch tính.
 */
function radar(components) {
  const axes = RADAR_AXES
    .map(([key, label]) => ({ label, c: components.find((x) => x.key === key) }))
    .filter((a) => a.c);

  if (axes.length < 3) return '';

  // Khung rộng hơn cao vì nhãn nằm NGANG hai bên: bản đầu dùng khung vuông 400×330 và hai nhãn
  // dưới bị cắt mất dòng số — điểm dưới cùng rơi ở y 328 rồi còn cộng thêm hai dòng tspan nữa.
  // Chỗ chừa ra mỗi bên phải đủ cho nhãn dài nhất, không phải chỉ cho vòng tròn.
  const W = 460, H = 320, cx = W / 2, cy = 161, R = 112, LR = R + 22;

  const at = (i, v) => {
    const a = (-90 + (i * 360) / axes.length) * Math.PI / 180;
    return [cx + Math.cos(a) * R * (v / 100), cy + Math.sin(a) * R * (v / 100)];
  };

  const poly = (v) => axes.map((_, i) => at(i, typeof v === 'function' ? v(i) : v).join(',')).join(' ');

  const rings = [25, 50, 75, 100].map((v) => `<polygon points="${poly(v)}"
    class="rd-ring${v === 50 ? ' rd-mid' : ''}"/>`).join('');

  const spokes = axes.map((_, i) => {
    const [x, y] = at(i, 100);
    return `<line x1="${cx}" y1="${cy}" x2="${x.toFixed(1)}" y2="${y.toFixed(1)}" class="rd-spoke"/>`;
  }).join('');

  const labels = axes.map((a, i) => {
    const [x, y] = at(i, 100);
    const dx = (x - cx) / R, dy = (y - cy) / R;
    const lx = cx + dx * LR;

    // Nhãn TRÊN phải đẩy lên đủ cho CẢ HAI dòng, nhãn DƯỚI đẩy xuống vừa đủ dòng đầu — vì dòng
    // thứ hai luôn nằm dưới dòng đầu 1,15em. Không tính riêng hai chiều thì nhãn dưới cùng bị
    // cắt mất dòng số, đúng lỗi của bản đầu.
    const ly = cy + dy * LR + (dy < -0.6 ? -14 : dy > 0.55 ? 12 : -5);
    const anchor = Math.abs(dx) < 0.3 ? 'middle' : dx > 0 ? 'start' : 'end';

    return `<text x="${lx.toFixed(1)}" y="${ly.toFixed(1)}" text-anchor="${anchor}" class="rd-lbl">
      <tspan x="${lx.toFixed(1)}">${esc(a.label)}</tspan>
      <tspan x="${lx.toFixed(1)}" dy="1.15em" class="rd-num">${a.c.median}</tspan>
    </text>`;
  }).join('');

  const dots = axes.map((a, i) => {
    const [x, y] = at(i, a.c.median);
    return `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="4.5" class="rd-dot"><title>${esc(a.label)}: phân vị ${a.c.median} qua ${n0(a.c.games)} ván${
      a.c.won === null || a.c.won === undefined ? '' : ` (thắng ${a.c.won}, thua ${a.c.lost})`}</title></circle>`;
  }).join('');

  const spoken = axes.map((a) => `${a.label} ${a.c.median}`).join(', ');

  // Nhãn "50" đặt NGAY TRÊN vòng đậm chứ không ở tâm: ở tâm nó nằm dưới đa giác dữ liệu và bị
  // lớp nền của đa giác làm mờ, đồng thời không chỉ vào thứ nó đang gọi tên.
  const [, midY] = at(0, 50);

  return `<svg class="rd" viewBox="0 0 ${W} ${H}" role="img"
      aria-label="Biểu đồ bảy trục, thang phân vị 0 đến 100 so với người chơi cùng hero. ${esc(spoken)}.">
    <g class="rd-grid">${rings}${spokes}</g>
    <polygon points="${poly((i) => axes[i].c.median)}" class="rd-area"/>
    ${dots}${labels}
    <text x="${cx + 7}" y="${(midY + 4).toFixed(1)}" class="rd-hint">50</text>
  </svg>
  <p class="desc" style="margin:var(--s-2) 0 0">Vòng đậm ở giữa là mức <b>50</b> — ngang người
    chơi trung bình trên cùng hero. Vươn ra ngoài vòng đó là hơn người, thụt vào trong là kém.
    Thang luôn chạy đủ 0→100 nên hai người so được hình với nhau.</p>`;
}

/* ---------------------- Điểm thành phần theo phân vị ---------------------- */

/**
 * Một cột kỹ năng. Thanh chạy 0→100 là phân vị so với người chơi CÙNG HERO, nên vạch 50 ở giữa
 * là mốc "ngang người trung bình" — vẽ hẳn vạch đó ra vì không có nó thì một thanh dài 60% trông
 * như thành tích tốt trong khi nó chỉ nhỉnh hơn trung bình chút xíu.
 *
 * Dải mờ là khoảng giữa ván dở (phân vị 25 của chính người này) và ván hay (phân vị 75) — thứ
 * cho biết người này ỔN ĐỊNH hay thất thường, mà một con số trung vị đơn lẻ giấu mất.
 */
function skillRow(c) {
  const tone = c.median >= 65 ? 'good' : c.median <= 35 ? 'bad' : 'mid';
  const delta = c.recent === null || c.recent === undefined ? null : c.recent - c.median;

  return `<div class="sk-row">
    <div class="sk-name">${esc(c.label)}<small>${n0(c.games)} ván${c.inverted ? ' · đã đảo chiều' : ''}</small></div>
    <div class="sk-track" role="img"
         aria-label="${esc(c.label)}: phân vị ${c.median} trên 100">
      <span class="sk-band" style="left:${c.low}%;width:${Math.max(c.high - c.low, 1)}%"></span>
      <span class="sk-fill sk-${tone}" style="width:${c.median}%"></span>
      <span class="sk-mark sk-${tone}" style="left:${c.median}%"></span>
      <span class="sk-avg"></span>
    </div>
    <div class="sk-val txt-${tone}">${c.median}</div>
    <div class="sk-delta">${delta === null ? ''
      : `<span class="${delta > 0 ? 'cal-good' : delta < 0 ? 'cal-bad' : 'mu'}"
              title="50 ván gần nhất so với toàn bộ lịch sử">${delta > 0 ? '▲' : delta < 0 ? '▼' : '·'} ${Math.abs(delta)}</span>`}</div>
    ${c.won === null || c.won === undefined || c.lost === null || c.lost === undefined ? ''
      // Thứ tự LUÔN là thắng trước, thua sau — và đó mới là kênh phân biệt chính, không phải
      // màu. Ở chế độ tối, xanh --pos và đỏ --neg chỉ cách nhau ΔE 3,3 với người mù màu đỏ-lục,
      // tức gần như cùng một màu. Màu ở đây chỉ là lớp củng cố cho người nhìn được nó.
      : `<div class="sk-split" title="Ván thắng ${c.won} · ván thua ${c.lost} (phân vị)">
           <span class="cal-good">${c.won}</span><i>·</i><span class="cal-bad">${c.lost}</span>
         </div>`}
  </div>`;
}

function skillBlock(d) {
  const all = d.components || [];
  if (!all.length) return '';

  const roles = d.componentsByRole || [];

  // Số ván trên chip là số ván CÓ PHÂN VỊ, không phải tổng số ván đã lưu: mỗi cột chỉ đếm những
  // ván có đúng chỉ số đó, và ván chưa lấy chi tiết thì không có phân vị nào cả.
  const rated = Math.max(...all.map((c) => c.games));

  const chips = [`<button type="button" class="chip on" data-role="">Tất cả<small>${n0(rated)}</small></button>`]
    .concat(roles.map((r) => `<button type="button" class="chip" data-role="${esc(r.role)}">
        ${esc(r.label)}<small>${n0(r.games)}</small></button>`));

  // Giải thích NGẮN ở đây, phần dài nằm trong khối "Cách tính" gập lại cuối trang. Ba đoạn văn
  // trước mỗi biểu đồ là cách chắc chắn để người đọc bỏ qua cả biểu đồ lẫn đoạn văn.
  return `<h3 style="margin-top:var(--s-5)">Điểm từng mặt, so với người chơi cùng hero</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Phân vị 0→100 so với người chơi <b>cùng hero</b>.
      Vạch giữa là mức 50 — ngang người trung bình. Hai số ngoài cùng bên phải luôn theo thứ tự
      <b>ván thắng trước, ván thua sau</b> (<span class="cal-good">60</span>·<span class="cal-bad">26</span>
      nghĩa là thắng 60, thua 26). Đọc chúng
      như bối cảnh, đừng đọc thành lời bào chữa: <b>mọi</b> chỉ số đều sụp khi thua, với mọi
      người, ở cùng một mức — đo trên hai tài khoản thì khoảng cách từng cột gần như trùng khít.
      "Giữ mạng" đã đảo chiều để cao luôn là tốt.</p>
    ${roles.length ? `<div class="chips" id="pf-roles">${chips.join('')}</div>` : ''}
    <div id="pf-radar">${radar(all)}</div>
    <div class="sk-list" id="pf-skills">${all.map(skillRow).join('')}</div>`;
}

/** Đổi chip vai trò thì vẽ lại đúng bộ cột của vai trò đó, không gọi lại máy chủ. */
function wireRoleChips(d) {
  const box = $('#pf-roles');
  const list = $('#pf-skills');
  if (!box || !list) return;

  box.addEventListener('click', (e) => {
    const btn = e.target.closest('.chip');
    if (!btn) return;

    box.querySelectorAll('.chip').forEach((c) => c.classList.toggle('on', c === btn));

    const role = btn.dataset.role;
    const found = role ? (d.componentsByRole || []).find((r) => r.role === role) : null;
    const rows = role ? (found ? found.components : []) : (d.components || []);

    // Ra-đa phải đổi theo cùng bộ lọc: để nó đứng yên trong khi bảng bên dưới đã đổi vai trò là
    // bày ra hai con số khác nhau cho cùng một thứ, và người đọc không có cách nào biết cái nào
    // đang nói về cái gì.
    const rd = $('#pf-radar');
    if (rd) rd.innerHTML = radar(rows);

    list.innerHTML = (rows.length
      ? rows.map(skillRow).join('')
      : '<div class="empty">Vai trò này chưa đủ ván có phân vị để chấm.</div>')
      // Mốc so KHÔNG đổi theo vai trò — nó luôn là mọi người chơi cùng hero đó. Không nói ra
      // thì một cột thấp ở hỗ trợ dễ bị đọc thành "chơi hỗ trợ tệ", trong khi phần lớn chênh
      // lệch đến từ việc hero hỗ trợ vốn farm ít hơn hero core.
      + (role && found
        ? `<p class="desc" style="margin-top:var(--s-3)">Đang lọc ${esc(found.label)}:
             ${n0(found.games)} ván có nhãn replay, thắng ${fmt(found.winrate, 1, '%')}. Mốc so
             vẫn là mọi người chơi cùng hero — không phải mọi người chơi cùng vị trí — nên hãy
             đọc theo chiều "so với người khác dùng đúng hero này".</p>`
        : '');
  });
}

/* --------------------------- Hero pool vs meta --------------------------- */

/**
 * Vì sao có thể không hero nào được đánh dấu ★.
 *
 * Một bảng toàn ô trống mà im lặng sẽ bị đọc thành "hệ thống hỏng" hoặc, tệ hơn, thành "vậy là
 * mình chơi hero nào cũng như nhau". Sự thật khác hẳn: mẫu mỗi hero còn quá mỏng so với mức
 * nhiễu, và câu này nói rõ cần bao nhiêu ván thì mới kết luận được.
 */
function noStarNote(d, rows) {
  if (rows.some((r) => r.notable)) return '';

  const need = d.noticeNeedsGames;
  const pool = d.noticePoolSize;
  if (!need || !pool) return '';

  return `<p class="desc" style="margin:0 0 var(--s-3)"><b>Chưa hero nào được đánh dấu ★ — và
    đó là kết luận, không phải lỗi.</b> Bạn chơi ${n0(pool)} hero, nên hero "nổi bật nhất" là cực
    trị của ${n0(pool)} phép so; chỉ riêng may rủi đã đủ tạo ra vài hero trông rất chênh. Để một
    cách biệt 15 điểm phần trăm đứng vững ở cỡ pool này cần khoảng <b>${n0(need)} ván trên cùng
    một hero</b>. Các con số trong bảng vẫn thật và vẫn đáng xem — chỉ là chưa đủ để tuyên bố.</p>`;
}

/* ------------------------------- Đồng đội ------------------------------- */

function matesBlock(d) {
  const mates = d.teammates || [];
  if (!mates.length) return '';

  return `<h3 style="margin-top:0">Chơi với ai thì thắng</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Cột quan trọng nhất là <b>Chênh</b>: tỷ lệ thắng
      khi có người đó, trừ đi tỷ lệ thắng ở những ván VẮNG họ. Không có phép trừ này thì "thắng
      62% khi chơi với A" chẳng nói lên điều gì — bạn có thể vẫn thắng 62% ở mọi ván khác.
      Dấu ★ là chênh lệch đã đủ lớn để không giải thích được bằng may rủi, sau khi đã tính tới
      việc bạn có nhiều đồng đội quen. Chỉ liệt kê người đã cùng nhóm từ
      ${n0(d.minPartyGames)} ván trở lên — người ghép trúng ngẫu nhiên không lên bảng, vừa vì họ
      không tự nguyện xuất hiện ở đây, vừa vì gặp lại ngẫu nhiên thì chẳng nói lên điều gì.</p>
    <div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Đồng đội</th><th scope="col">Hạng</th>
        <th scope="col">Ván chung</th><th scope="col">Cùng nhóm</th>
        <th scope="col">Thắng khi có</th><th scope="col">Thắng khi vắng</th>
        <th scope="col">Chênh</th>
      </tr></thead>
      <tbody>${mates.map((t) => `<tr>
        <td>${esc(t.name)}${t.notable ? ' <b title="Chênh lệch đã vượt ngưỡng nhiễu">★</b>' : ''}</td>
        <td class="mu">${esc(t.rank || '—')}</td>
        <td class="num">${n0(t.games)}</td>
        <td class="num mu">${n0(t.partyGames)}</td>
        <td class="num">${fmt(t.winrate, 1, '%')}</td>
        <td class="num mu">${fmt(t.withoutWinrate, 1, '%')} <small>(${n0(t.withoutGames)})</small></td>
        <td class="num ${t.lift > 0 ? 'cal-good' : t.lift < 0 ? 'cal-bad' : ''}">${signed(t.lift, 1)}</td>
      </tr>`).join('')}</tbody>
    </table></div>`;
}

/* --------------------- Vai trò dịch chuyển qua các năm --------------------- */

const LANE_PARTS = [
  ['mid', 'Mid', 'era-mid'],
  ['safe', 'Safelane', 'era-safe'],
  ['off', 'Offlane', 'era-off'],
  ['jungle', 'Rừng', 'era-jgl'],
];

function eraBlock(d) {
  const eras = d.roleEras || [];
  if (eras.length < 2) return '';

  return `<h3 style="margin-top:var(--s-5)">Vai trò dịch chuyển qua các năm</h3>
    <p class="desc" style="margin:0 0 var(--s-3)">Chỉ đếm những ván có nhãn vị trí THẬT đọc từ
      replay — phần suy đoán không phân biệt được mid với offlane nên đưa vào đây sẽ tạo ra một
      biểu đồ đầy đặn mà bịa. Đổi lại số ván có nhãn rất mỏng, nên mỗi năm đều ghi rõ nó dựng
      trên bao nhiêu ván; năm mờ là năm dưới 10 ván có nhãn.</p>
    <div class="era-list">${eras.map((e) => {
      const parts = LANE_PARTS
        .filter(([k]) => e[k] > 0)
        .map(([k, label, cls]) => `<span class="era-seg ${cls}"
            style="width:${(e[k] / e.labelled) * 100}%"
            title="${label}: ${n0(e[k])}/${n0(e.labelled)} ván"></span>`).join('');

      return `<div class="era-row${e.thin ? ' thin' : ''}">
        <div class="era-year">${e.year}</div>
        <div class="era-bar">${parts}</div>
        <div class="era-n mu">${n0(e.labelled)}<small>/${n0(e.games)}</small></div>
      </div>`;
    }).join('')}</div>
    <div class="era-key">${LANE_PARTS.map(([, label, cls]) =>
      `<span><i class="${cls}"></i>${label}</span>`).join('')}
      <span class="mu">Số bên phải: ván có nhãn / tổng ván của năm.</span></div>`;
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

    // Dùng CHUNG kiểu thẻ với tier list tính từ dữ liệu: chân dung dọc, tên nằm DƯỚI ảnh.
    // Bản trước dùng ảnh khổ ngang "crops/" và đè tên lên trên — chữ trắng trên nền tuỳ ảnh
    // nên chỗ đọc được chỗ không, và hai tab cùng là tier list lại trông như hai trang khác nhau.
    const cards = heroes.map((h) => {
      const dim = q && !h.n.toLowerCase().includes(q) ? ' dim' : '';
      const title = `${h.n}${h.w ? ` · winrate pub ${h.w}%` : ''}${h.t ? `\n${h.t}` : ''}`;
      return `<div class="tl-hero${dim}" title="${esc(title)}">
        ${heroImg(HERO_CDN + h.i + '.png', 58, 33)}
        <span class="tl-name">${esc(h.n)}</span>
        <span class="tl-num">${h.w ? `pub ${h.w}%` : ''}</span>
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
  // Không còn nút 30/90/180 ngày. "Phong độ" nghĩa là phong độ HIỆN TẠI, nên cửa sổ 30 ngày
  // là câu trả lời — bày ba nút là bắt người đọc tự so ba biểu đồ rồi tự kết luận.
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
    const d = await getJson(`api/trend?window=${state.formWindow}`);
    renderForm(d.points || [], d);
  } catch (err) {
    body.className = '';
    body.innerHTML = `<div class="error">Không tải được <code>api/trend</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

function renderForm(rows, meta) {
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
    <p class="desc" style="margin-top:var(--s-3)">Bấm vào tên đội để ẩn/hiện đường của đội đó.</p>
    ${verdictTable(meta)}
    <div id="insight-body"></div>`;

  loadInsights();

  $$('#form-body .legend button').forEach((btn) => {
    btn.onclick = () => {
      const slug = btn.dataset.slug;
      if (state.formHidden.has(slug)) state.formHidden.delete(slug);
      else state.formHidden.add(slug);
      renderForm(rows, meta);
    };
  });
}

/**
 * Nhận định chi tiết theo từng đội.
 *
 * Bảng xu hướng phía trên chỉ trả lời được một câu: Elo/winrate đang lên hay xuống. Mọi thứ
 * khác — đội này thắng nhờ đâu, có tận dụng được lợi thế đầu trận không, đội hình đã ăn ý
 * chưa, ai đang khắc chế ai — đều nằm sẵn trong dữ liệu nhưng trước đây bắt người đọc tự ghép.
 *
 * Đội KHÔNG có nhận định nào vẫn được hiện, kèm lý do. Ẩn đi thì người đọc không phân biệt
 * được "không có gì đáng nói" với "hỏng, không tải được".
 */
let insightsLoaded = false;

async function loadInsights() {
  const body = $('#insight-body');
  if (!body) return;

  if (insightsLoaded) { renderInsights(insightsLoaded); return; }
  body.innerHTML = '<div class="skeleton" style="height:160px;margin-top:var(--s-5)"></div>';

  try {
    insightsLoaded = await getJson('api/insights');
    renderInsights(insightsLoaded);
  } catch (err) {
    body.innerHTML = `<div class="error" style="margin-top:var(--s-5)">Không tải được
      <code>api/insights</code>.<br><small>${esc(err.message)}</small></div>`;
  }
}

const TONE_ICON = {
  good: '<path d="M20 6L9 17l-5-5"/>',
  bad: '<path d="M18 6L6 18M6 6l12 12"/>',
  warn: '<path d="M12 9v5M12 17.5v.01M10.3 3.9L1.8 18a2 2 0 001.7 3h17a2 2 0 001.7-3L14.7 3.9a2 2 0 00-3.4 0z"/>',
  flat: '<path d="M5 12h14"/>',
};

function renderInsights(data) {
  const body = $('#insight-body');
  if (!body) return;

  const teams = (data.teams || []).filter((t) => t.insights.length > 0);
  const quiet = (data.teams || []).filter((t) => t.insights.length === 0);

  if (!teams.length && !quiet.length) {
    body.innerHTML = '';
    return;
  }

  const card = (t) => `
    <article class="insight-card">
      <header>
        ${teamLogo(state.teams.find((x) => x.slug === t.slug) || { name: t.name }, 26)}
        <div>
          <strong>${esc(t.name)}</strong>
          <div class="insight-sub">${n0(t.lineupGames)} ván đúng đội hình${
            t.lineupSince ? ` · từ ${esc(t.lineupSince)}` : ''}</div>
        </div>
      </header>
      <ul>${t.insights.map((i) => `
        <li class="tone-${esc(i.tone)}">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"
               stroke-linecap="round" stroke-linejoin="round">${TONE_ICON[i.tone] || TONE_ICON.flat}</svg>
          <span>${esc(i.text)}</span>
        </li>`).join('')}</ul>
    </article>`;

  body.innerHTML = `
    <h3 style="margin-top:var(--s-6)">Hệ thống đọc được gì về từng đội</h3>
    <p class="desc" style="margin:0 0 var(--s-4)">${esc(data.method || '')}</p>
    <div class="insight-grid">${teams.map(card).join('')}</div>
    ${quiet.length ? `<p class="desc" style="margin-top:var(--s-4)">
       Không có nhận định nào vượt ngưỡng nhiễu cho ${n0(quiet.length)} đội:
       <b>${quiet.map((t) => esc(t.name)).join(', ')}</b>.</p>` : ''}`;
}

/**
 * Nhận định của hệ thống, đặt NGAY DƯỚI biểu đồ.
 *
 * Biểu đồ bày dữ liệu; bảng này trả lời. Hai người nhìn cùng một đường có thể kết luận khác
 * nhau, nên việc đọc dốc là việc của máy — và máy nói rõ nó dựa vào đâu: tổng thay đổi so với
 * chính độ nhiễu của chuỗi.
 */
function verdictTable(meta) {
  const v = meta && meta.verdicts;
  if (!v || !v.length) return '';

  const chip = (x) => {
    const cls = x.direction === 'đang lên' ? 'up' : x.direction === 'đang xuống' ? 'down' : '';
    return `<span class="trend ${cls}">${esc(x.direction)}</span>`;
  };

  // Chỉ nêu đội THẬT SỰ có chuyển biến. Liệt kê cả 16 đội với 13 dòng "đi ngang" là quay lại
  // đúng vấn đề cũ: bắt người đọc tự lọc.
  const moving = v.filter((x) => x.elo.direction === 'đang lên' || x.elo.direction === 'đang xuống');

  if (!moving.length) {
    return `<div class="note" style="margin-top:var(--s-4)">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div><b>Không đội nào có xu hướng tách được khỏi nhiễu.</b><br>${esc(meta.method || '')}</div>
    </div>`;
  }

  return `<h3 style="margin-top:var(--s-5)">Hệ thống nhận định</h3>
    <div class="table-scroll"><table>
      <thead><tr>
        <th scope="col">Đội</th><th scope="col">Elo</th>
        <th scope="col">Thay đổi</th><th scope="col">So với nhiễu</th><th scope="col">Winrate</th>
      </tr></thead>
      <tbody>${moving.map((x) => `<tr>
        <td>${esc(x.teamName)}</td>
        <td>${chip(x.elo)}</td>
        <td class="num">${signed(x.elo.change, 1)}</td>
        <td class="num">${fmt(x.elo.ratio, 1)}×</td>
        <td>${chip(x.winrate)}</td>
      </tr>`).join('')}</tbody>
    </table></div>
    <div class="note" style="margin-top:var(--s-3)">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div>Chỉ nêu đội có xu hướng tách được khỏi nhiễu — ${v.length - moving.length}/${v.length}
      đội còn lại đang đi ngang.<br><br>${esc(meta.method || '')}</div>
    </div>`;
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
  enhanceSelect(a, 'Gõ để tìm đội…');
  enhanceSelect(b, 'Gõ để tìm đội…');

  loadPredict();
  loadCalibration();
  loadLedger();
  loadPatches();
}

/**
 * Sổ theo dõi. Khi chưa có dòng nào thì vẫn phải nói rõ ĐANG GHI TỪ BAO GIỜ — một mục trống
 * không kèm ngày mở sổ trông y hệt một tính năng hỏng.
 */
async function loadLedger() {
  const body = $('#ledger-body');
  body.innerHTML = '<div class="skeleton" style="height:150px"></div>';

  try {
    const d = await getJson('api/ledger');
    const since = d.recordingSince
      ? new Date(d.recordingSince).toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' })
      : null;

    if (!d.ready) {
      body.innerHTML = `<div class="empty">${esc(d.note || '')}</div>
        <div class="bento" style="margin-top:var(--s-4)">
          <article class="kpi p2">
            <div class="kpi-label">Đang chờ kết quả</div>
            <div class="kpi-value">${d.open}</div>
            <div class="kpi-note">${since ? 'mở sổ từ ' + esc(since) : 'chưa mở sổ'}</div>
          </article>
        </div>`;
      return;
    }

    // Cột "mô hình nói" và "thực tế" đặt cạnh nhau: lệch bao nhiêu mới là câu trả lời,
    // chứ không phải từng con số riêng lẻ.
    const rows = d.buckets.map((b) => `<tr>
      <td>${esc(b.band)}</td>
      <td class="num">${b.said}%</td>
      <td class="num">${b.actual}%</td>
      <td class="num">${(b.actual - b.said).toFixed(1)}</td>
      <td class="num">${n0(b.count)}</td>
    </tr>`).join('');

    body.innerHTML = `
      <div class="bento">
        <article class="kpi p2">
          <div class="kpi-label">Điểm Brier</div>
          <div class="kpi-value">${d.brier}</div>
          <div class="kpi-note">0 là hoàn hảo · đoán bừa 50% ra 0,25</div>
        </article>
        <article class="kpi p2">
          <div class="kpi-label">Đoán đúng bên thắng</div>
          <div class="kpi-value">${d.accuracy}%</div>
          <div class="kpi-note">${d.resolved} dự đoán đã chấm · ${d.open} đang chờ</div>
        </article>
      </div>

      ${d.caveat ? `<div class="note warn" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div>${esc(d.caveat)}</div>
      </div>` : ''}

      <h3 style="margin-top:var(--s-5)">Nói bao nhiêu, thực tế bao nhiêu</h3>
      <div class="table-scroll"><table>
        <thead><tr>
          <th scope="col">Mức tự tin</th><th scope="col">Mô hình nói</th>
          <th scope="col">Thực tế</th><th scope="col">Lệch</th><th scope="col">Số trận</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>Mở sổ từ ${esc(since || '—')}. ${esc(d.note || '')}</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
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

  // Đội chưa đủ ván với đội hình TI2026 thì KHÔNG có Elo. Tách ra chứ không bỏ đi: một đội
  // biến mất khỏi bảng mà không dòng nào giải thích là đúng loại lỗi đang phải sửa. Và cũng
  // không được trộn vào phép tính min/max — elo null sẽ kéo đáy thang xuống 0 và mọi thanh bar
  // dài như nhau.
  const ranked = rows.filter((r) => r.elo !== null && r.elo !== undefined);
  const unranked = rows.filter((r) => r.elo === null || r.elo === undefined);

  const max = ranked.length ? Math.max(...ranked.map((r) => r.elo)) : 0;
  const min = ranked.length ? Math.min(...ranked.map((r) => r.elo)) : 0;
  const span = Math.max(max - min, 1);

  const rankedHtml = ranked.map((r, i) => {
    const width = Math.max(((r.elo - min) / span) * 100, 3);
    // Đội ít ván: Elo dao động mạnh vì mỗi trận đổi tới 24 điểm
    const thin = r.eloGames !== undefined && r.eloGames !== null
      ? r.eloGames < MIN_MAPS_FOR_LEADERBOARD
      : r.maps < MIN_MAPS_FOR_LEADERBOARD;
    return `<div class="rank-row">
      <span class="rank-no num">${i + 1}</span>
      <div class="rank-team">
        ${teamLogo({ name: r.teamName, logo: r.logo })}
        <span class="rank-name">${esc(r.teamName)}</span>
        ${thin ? `<span class="rank-thin" title="Ít ván nên Elo còn dao động">${n0(r.eloGames ?? r.maps)} ván</span>` : ''}
      </div>
      <div class="elo-track"><span class="elo-bar" style="width:${width}%"></span></div>
      <span class="rank-val num">${fmt(r.elo, 0)}<small class="elo-wr">${fmt(r.winrate, 0)}%</small></span>
    </div>`;
  }).join('');

  const unrankedHtml = unranked.length
    ? `<div class="rank-unranked">
         <h4>Chưa xếp được hạng</h4>
         ${unranked.map((r) => `<div class="rank-row">
           <span class="rank-no num">—</span>
           <div class="rank-team">
             ${teamLogo({ name: r.teamName, logo: r.logo })}
             <span class="rank-name">${esc(r.teamName)}</span>
           </div>
           <div class="elo-track"></div>
           <span class="rank-val num mu">${n0(r.eloGames ?? 0)} ván</span>
         </div>`).join('')}
         <p class="desc" style="margin-top:var(--s-2)">Đội hình TI2026 của các đội này chưa đá
           đủ ván với một đội hình TI2026 khác. Elo khởi điểm ở 1500 nên nếu vẫn hiện số, họ sẽ
           nằm đúng giữa bảng và trông như đội trung bình — trong khi thật ra là <b>chưa
           biết</b>.</p>
       </div>`
    : '';

  $('#ratings-body').innerHTML = rankedHtml + unrankedHtml + `<p class="desc" style="margin-top:var(--s-3)">
      Chỉ tính ván mà <b>cả hai bên</b> đều ra sân đúng đội hình TI2026 — Elo là số so sánh giữa
      hai đội, chấm đội hôm nay bằng một trận của đội hình cũ thì sai cả hai phía.
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

  // Nói RÕ đội nào thiếu và thiếu gì. "Chưa đủ dữ liệu Elo cho một trong hai đội" bắt người
  // đọc tự đoán là đội nào, và đoán sai thì họ kết luận sai về cả hai.
  const noElo = [p.teamA, p.teamB].filter((t) => t.elo === null || t.elo === undefined);

  const head = pa === null
    ? `<div class="empty"><b>${noElo.map((t) => esc(t.name)).join(' và ')}</b>
         chưa đá đủ ván với đội hình TI2026${noElo.some((t) => t.eloGames != null)
           ? ` (${noElo.map((t) => `${esc(t.name)}: ${t.eloGames ?? 0} ván`).join(', ')})`
           : ''} nên chưa có Elo — đưa ra tỷ lệ thắng lúc này là bịa một con số.
         Phần đối đầu và kèo tài/xỉu bên dưới vẫn dùng được.</div>`
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

  // Con số LỚN phải là con số đáng dùng — tức là tính trên tập ván mà cả hai bên vẫn là đội
  // hình TI2026. Để nguyên tổng cả lịch sử ở vị trí này là đem thành tích của một đội khác mang
  // cùng tên ra làm căn cứ dự đoán.
  const lv = p.headToHead.lineup;
  const h2h = p.headToHead.played > 0
    ? `<div class="pred-block">
         <h3>Đối đầu trực tiếp</h3>
         ${lv && lv.games > 0
           ? `<p class="pred-h2h num"><b>${lv.winsA}</b> – <b>${lv.winsB}</b>
                <span class="mu">sau ${n0(lv.games)} ván đúng đội hình</span></p>`
           : `<p class="pred-h2h num mu">chưa có ván nào đúng đội hình</p>`}
         ${lv ? `<p class="desc" style="margin:0 0 var(--s-3)">${esc(lv.text)}</p>` : ''}
         ${p.headToHead.recent.map((m) => {
           const off = m.keptA !== undefined && Math.min(m.keptA, m.keptB) < 5;
           return `<div class="pred-hist${off ? ' off-lineup' : ''}">
             <span>${esc(m.date)}${off ? ` <span class="kept swap">${m.keptA}/${m.keptB}</span>` : ''}</span>
             <span class="${m.aWon ? 'pos' : 'neg'}">${m.aWon ? esc(p.teamA.name) : esc(p.teamB.name)} thắng</span>
             <span class="num">${esc(m.score)}</span>
           </div>`;
         }).join('')}
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
         <b>${fmt(d.p25, 1)}–${fmt(d.p75, 1)}${unit}</b> · dải ${fmt(d.min, 1)}–${fmt(d.max, 1)}${unit} qua ${n0(d.count)} ván</p>
      ${rows}
    </div>`;
  }
}

/* ============================ Biến động ============================ */

function setupChanges() {
  loadChanges();
  loadSeries();
}

/**
 * Không còn nút chọn mốc thời gian.
 *
 * Bản trước có 1/7/30 ngày, tức bắt người đọc mở ba lần rồi tự nhớ cái nào có gì — trong khi
 * câu hỏi của họ chỉ có một: "có gì mới không?". Giờ máy chủ rà cả bốn mốc và trả về mỗi biến
 * động một lần, kèm NHỊP ĐỘ để biết đó là cú nhảy đột ngột hay xu hướng trôi chậm.
 */
async function loadChanges() {
  const body = $('#changes-body');
  body.innerHTML = '<div class="skeleton" style="height:160px"></div>';

  try {
    const r = await getJson('api/changes');

    if (!r.changes || !r.changes.length) {
      body.innerHTML = `<div class="empty">${esc(r.note || 'Không có biến động nào vượt ngưỡng đáng chú ý.')}</div>`;
      return;
    }

    body.innerHTML = `<p class="desc" style="margin-bottom:var(--s-3)">
        ${n0(r.changes.length)} biến động, tự rà ${(r.horizons || []).map((d) => d + ' ngày').join(' · ')}
        · tính tới <b>${esc(r.latest)}</b></p>` +
      r.changes.map((c) => `<div class="change ${c.improved ? 'up' : 'down'}">
          <span class="change-arrow" aria-hidden="true">${c.improved ? '▲' : '▼'}</span>
          <div>
            <div class="change-text">${esc(c.narrative)}
              <span class="chip" style="margin-left:var(--s-2)">${esc(c.pace)} · ${c.horizonDays} ngày</span></div>
            <div class="change-meta num">${esc(c.label)} · ${c.before} → ${c.after}
              · mạnh gấp ${c.magnitude}× ngưỡng · mốc so sánh ${esc(c.baseline)}</div>
          </div>
        </div>`).join('') +
      `<div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(r.method || '')}${r.pending ? '<br><br>' + esc(r.pending) : ''}</div>
      </div>`;
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
        <div class="kpi-note">${n0(s.gamesInSeries)} ván nằm trong series</div>
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
  enhanceSelect(sel, 'Gõ để tìm đội…');
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
        <td class="num">${n0(p.games)}</td>
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
      // Cùng kiểu thẻ với hai tier list — ba chỗ hiện hero thì phải trông như một
      return `<div class="tl-hero ${tone}" title="${esc(name)} · ${h.games} ván · winrate ${h.winrate}%">
        ${heroImg(img ? HERO_CDN + img + '.png' : null, 58, 33)}
        <span class="tl-name">${esc(name)}</span>
        <span class="tl-num">${n0(h.games)} ván · ${fmt(h.winrate, 0)}%</span>
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
      <td class="num">${n0(h.games)}</td>
      <td class="num">${fmt(h.winrate, 0)}%</td>
      <td class="who">${(h.who || []).map((w) => esc(w)).join(', ')}</td>
    </tr>`).join('');

    body.innerHTML = `
      <div class="bento">
        <article class="kpi p2">
          <div class="kpi-label">Ván pub đã ghi</div>
          <div class="kpi-value">${n0(d.totalGames)}</div>
          <div class="kpi-note">${d.days} ngày gần nhất</div>
        </article>
        <article class="kpi p4">
          <div class="kpi-label">Tuyển thủ có dữ liệu</div>
          <div class="kpi-value">${n0(d.totalPlayers)}</div>
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
      </div>
      ${biasNote(d.bias)}`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được <code>api/pro-pub</code>.<br><small>${esc(err.serverMessage || err.message)}</small></div>`;
  }
}

/* ============================ Trạng thái cập nhật ============================ */

/** "3 phút nữa" / "12 phút trước". Mốc tuyệt đối vô dụng khi người đọc ở múi giờ khác. */
function relTime(iso, now) {
  if (!iso) return null;
  const diff = (new Date(iso) - now) / 1000;
  const abs = Math.abs(diff);

  const say = abs < 90 ? `${Math.round(abs)} giây`
    : abs < 5400 ? `${Math.round(abs / 60)} phút`
    : abs < 172800 ? `${Math.round(abs / 3600)} giờ`
    : `${Math.round(abs / 86400)} ngày`;

  return diff >= 0 ? `${say} nữa` : `${say} trước`;
}

function fmtClock(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

const RUN_TONE = { Succeeded: 'ok', Failed: 'bad', Running: 'run', Interrupted: 'bad' };

async function loadSchedule() {
  const body = $('#sched-body');
  if (!body) return;
  body.innerHTML = '<div class="skeleton" style="height:200px"></div>';

  try {
    const d = await getJson('api/ingest/status');

    // Dùng giờ CỦA MÁY CHỦ làm mốc, không dùng giờ máy người đọc: đồng hồ lệch vài phút là
    // chuyện thường, và "chạy tiếp sau -3 phút" trông như hỏng.
    const now = new Date(d.serverTime);

    const bf = d.backfill || {};
    const busy = d.running;

    const state = busy
      ? ['Đang chạy', 'run', 'Một vòng nạp đang diễn ra']
      : d.enabled
        ? ['Đang chờ', 'ok', `Chạy mỗi ${d.intervalHours} giờ`]
        : ['Đã tắt', 'bad', 'Chỉ cập nhật khi chạy tay'];

    const rows = (d.runs || []).map((r) => `<tr>
      <td><span class="run-dot ${RUN_TONE[r.status] || ''}" aria-hidden="true"></span>${esc(r.source)}</td>
      <td>${esc(r.status === 'Succeeded' ? 'Xong' : r.status === 'Failed' ? 'Lỗi' : r.status === 'Interrupted' ? 'Gián đoạn' : 'Đang chạy')}</td>
      <td class="num">${fmtClock(r.startedAt)}</td>
      <td class="num">${r.finishedAt
        ? Math.max(1, Math.round((new Date(r.finishedAt) - new Date(r.startedAt)) / 1000)) + 's'
        : '—'}</td>
      <td class="num">${r.itemsWritten}</td>
      <td class="run-err">${r.error ? esc(r.error) : ''}</td>
    </tr>`).join('');

    body.innerHTML = `
      <div class="bento">
        <article class="kpi p2">
          <div class="kpi-label">Trạng thái</div>
          <div class="kpi-value"><span class="run-dot ${state[1]}" aria-hidden="true"></span>${state[0]}</div>
          <div class="kpi-note">${esc(state[2])}</div>
        </article>
        <article class="kpi p3">
          <div class="kpi-label">${busy ? 'Bắt đầu lúc' : 'Chạy tiếp'}</div>
          <div class="kpi-value">${busy
            ? fmtClock(d.lastStartedAt)
            : (relTime(d.nextRunAt, now) || '—')}</div>
          <div class="kpi-note">${busy
            ? (relTime(d.lastStartedAt, now) || '')
            : (d.nextRunAt ? fmtClock(d.nextRunAt) : 'chưa lên lịch')}</div>
        </article>
        <article class="kpi p5">
          <div class="kpi-label">Đã nạp chi tiết</div>
          <div class="kpi-value">${bf.donePercent}%</div>
          <div class="kpi-note">${(bf.total - bf.pending).toLocaleString('vi-VN')} / ${(bf.total || 0).toLocaleString('vi-VN')} ván</div>
        </article>
      </div>

      ${bf.pending > 0 ? `
      <div class="progress" role="progressbar" aria-valuenow="${bf.donePercent}"
           aria-valuemin="0" aria-valuemax="100" aria-label="Tiến độ nạp chi tiết trận">
        <span style="width:${bf.donePercent}%"></span>
      </div>
      <p class="desc" style="margin:var(--s-2) 0 var(--s-4)">Còn <b>${bf.pending.toLocaleString('vi-VN')}</b> ván
      cần nạp lại ở phiên bản dữ liệu ${bf.schemaVersion}, tối đa ${bf.perRun} ván mỗi vòng —
      khoảng <b>${Math.ceil(bf.pending / bf.perRun)}</b> vòng nữa${bf.estimatedCostUsd
        ? `, chi phí ước tính <b>$${bf.estimatedCostUsd.toFixed(2)}</b>` : ''}.</p>

      ${bf.needsApproval ? `<div class="note warn" style="margin-bottom:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div><b>Đợt nạp này vượt ngưỡng $${bf.approvalThresholdUsd} — cần duyệt trước.</b><br>
        Ước tính $${bf.estimatedCostUsd.toFixed(2)} cho ${bf.pending.toLocaleString('vi-VN')} ván.
        Ngưỡng nằm ở <code>Ti2026__OpenDota__ApprovalThresholdUsd</code>.</div>
      </div>` : ''}` : ''}

      ${d.note ? `<div class="note warn"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg><div>${esc(d.note)}</div></div>` : ''}

      <div class="table-scroll"><table>
        <caption class="sr-only">Lịch sử các vòng nạp dữ liệu gần đây</caption>
        <thead><tr>
          <th scope="col">Nguồn</th><th scope="col">Kết quả</th><th scope="col">Bắt đầu</th>
          <th scope="col">Mất</th><th scope="col">Ghi</th><th scope="col">Lỗi</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">Không tải được trạng thái.<br><small>${esc(err.serverMessage || err.message)}</small></div>`;
  }
}

/* ============================ Tier list tính động ============================ */

/**
 * days: cửa sổ thời gian ÁP THÊM lên bộ lọc bản game.
 *
 * Cần nó vì chỉ số bản game của OpenDota không phân biệt 7.41a với 7.41e — cả họ dùng chung
 * một số, nên "toàn bản" là gần năm tháng meta. Lina là ví dụ thật: không ai chọn từ tháng Ba
 * tới tháng Sáu, rồi thành chủ lực mid từ tháng Bảy. Bình quân cả bản dìm cô từ hạng 10 xuống
 * hạng 52 — tức từ tier S/A xuống tier C.
 */
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
      ${d.thinSample ? `<div class="note warn" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div>${esc(d.thinSample)}</div>
      </div>` : ''}

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div><b>${esc(d.patch)}</b> · ${d.draftsAnalysed} bàn draft
        · mẫu hiệu dụng ${d.effectiveMatches} bàn.
        ${d.positionName ? `Vị trí <b>${esc(d.positionName)}</b> — ${esc(d.positionDesc || '')}.` : ''}
        <br><br>${esc(d.method || '')}
        <br><br>${esc(d.windowNote || '')}
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
        <td class="num">${fmt(l.winrate, 0)}%</td>
        <td class="num">${n0(l.games)}</td>
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

/* ============================ Fantasy ============================ */

/** Dải cảnh báo thiên lệch — hiện ở cả ba mục vì cả ba đều bị ảnh hưởng. */
/**
 * Hai loại sai, và phải nói riêng ra.
 *
 * "Chưa có nguồn" thì chỉ số bị bỏ hẳn khỏi phép tính. "Nguồn gần đúng" thì chỉ số VẪN được
 * tính và hiện ra một con số trông y hệt số đo thật — loại này khó thấy hơn nhiều, nên gộp
 * cả hai vào một dòng "5/18" là giấu mất đúng cái cần nói.
 */
function biasNote(bias) {
  if (!bias) return "";
  const warn = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' "
    + "stroke-linecap='round'><path d='M12 3l9 16H3z'/><path d='M12 9v4M12 16.5v.01'/></svg>";
  let html = "";

  if (bias.count) {
    const groups = (bias.byColor || [])
      .map((c) => "<b>" + esc(c.colorLabel) + "</b>: " + c.stats.map((x) => esc(x)).join(", "))
      .join("<br>");

    html += "<div class='note warn' style='margin-top:var(--s-4)'>" + warn
      + "<div><b>" + bias.count + "/" + bias.total + " chỉ số chưa có nguồn dữ liệu.</b><br>" + groups
      + "<br><br>" + esc(bias.message || "") + "</div></div>";
  }

  const ap = bias.approximate;
  if (ap && ap.count) {
    html += "<div class='note' style='margin-top:var(--s-4)'>" + warn
      + "<div><b>" + ap.count + " chỉ số dùng nguồn gần đúng: "
      + ap.stats.map((s) => esc(s.label)).join(", ") + ".</b><br>"
      + esc(ap.message) + "</div></div>";
  }

  return html;
}

async function loadFantasy() {
  loadFantasyConfig();
  loadFantasyRoster();
  loadFantasyPlayers();
  loadFantasyBaseline();
  loadFantasyCalc();
  loadFantasyTitles();
}

/**
 * Dải trạng thái ở đầu tab.
 *
 * Nó tồn tại để trả lời đúng một câu: trang này đang tính hay đang chờ? Không có nó thì ba
 * thẻ trống bên dưới trông như lỗi, chứ không phải như "chưa tới lúc".
 */
/* ------------------------------- Danh hiệu ------------------------------- */

async function loadFantasyTitles() {
  const body = $('#fantasy-titles');
  body.innerHTML = '<div class="skeleton" style="height:200px"></div>';

  try {
    // Prefix nằm ở /optimize vì nó phụ thuộc đội hình; suffix ở /titles vì nó không. Nhưng
    // với người đọc thì cả hai đều là "danh hiệu", nên phải hiện cùng một chỗ.
    const [d, opt] = await Promise.all([getJson('api/fantasy/titles'), getOptimize()]);
    if (!d.ready) { body.innerHTML = `<div class="empty">${esc(d.note || '')}</div>`; return; }

    // Nhóm quyết định cách đọc con số: 'né ra' nghĩa là xác suất CAO là tin xấu, nên nó phải
    // trông khác hẳn nhóm 'ổn định' chứ không cùng một màu chữ.
    const groupLabel = {
      'on-dinh': '<span class="chip">ổn định</span>',
      'ne-ra': '<span class="chip warn">nên né</span>',
      'hen-xui': '<span class="chip">hên xui</span>',
    };

    const pct = (x) => x === null || x === undefined ? '<span class="na">—</span>' : (x * 100).toFixed(1) + '%';

    const rows = d.suffixes.map((s) => `<tr>
      <td>${esc(s.label)}</td>
      <td>${groupLabel[s.group] || ''}</td>
      <td class="num">+${s.bonusPercent}%</td>
      <td class="num">${pct(s.probability)}</td>
      <td class="num"><b>${s.expectedBonusPercent === null || s.expectedBonusPercent === undefined
        ? '<span class="na">không đo được</span>' : '+' + s.expectedBonusPercent + '%'}</b></td>
      <td class="num">${s.sample ? s.hits + '/' + s.sample : '<span class="na">—</span>'}</td>
      <td>${esc(s.condition || '')}</td>
    </tr>`).join('');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Xác suất và lợi kỳ vọng của từng suffix</caption>
        <thead><tr>
          <th scope="col">Suffix</th><th scope="col">Nhóm</th><th scope="col">Thưởng</th>
          <th scope="col">Xác suất</th><th scope="col">Lợi kỳ vọng</th>
          <th scope="col">Số ván</th><th scope="col">Điều kiện</th>
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>Đo trên <b>${d.sampleGames}</b> ván. ${esc(d.suffixNote || '')}</div>
      </div>

      ${opt ? titleNote(opt) : ''}

      <div class="note warn" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div><b>Prefix và suffix KHÔNG cùng chất lượng bằng chứng.</b><br>${esc(d.prefixNote || '')}
        Đang có dữ liệu hero pool của <b>${d.prefixPlayers}</b> tuyển thủ.</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

/* ---------------------------- Máy tính emblem ---------------------------- */

/**
 * Bộ emblem người dùng đang dựng. Giữ ở đây thay vì đọc lại từ DOM mỗi lần tính: ô nào cũng có
 * ba thứ (chỉ số, tier, trait) và chúng ảnh hưởng lẫn nhau, nên một nguồn sự thật duy nhất.
 */
const calcState = { playerId: null, slots: [] };

async function loadFantasyCalc() {
  const body = $('#fantasy-calc');
  body.innerHTML = '<div class="skeleton" style="height:180px"></div>';

  try {
    const [cfg, players] = await Promise.all([
      getJson('api/fantasy/config'),
      getJson('api/fantasy/players'),
    ]);

    if (!cfg.ready || !players.ready || !players.players?.length) {
      body.innerHTML = '<div class="empty">Chưa đủ dữ liệu để tính.</div>';
      return;
    }

    calcState.cfg = cfg;
    calcState.players = players.players;

    // Mặc định lấy người đứng đầu và chính ba ô banner mà bảng xếp hạng đã chọn cho họ —
    // để mở lên là đã thấy một ví dụ thật, không phải một biểu mẫu trống.
    const first = players.players.find((p) => p.banner?.slots?.length) || players.players[0];
    calcState.playerId = first.playerId;
    calcState.slots = (first.banner?.slots || []).map((s) => ({
      statKey: s.statKey, tier: 'I', trait: 'none',
    }));

    renderCalc();
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

function renderCalc() {
  const body = $('#fantasy-calc');
  const cfg = calcState.cfg;
  const traits = Object.entries(cfg.traits || {}).filter(([k]) => !k.startsWith('_'));
  const tiers = Object.entries(cfg.tiers || {}).filter(([k]) => !k.startsWith('_'));

  const playerOpts = calcState.players
    .map((p) => `<option value="${p.playerId}"${p.playerId === calcState.playerId ? ' selected' : ''}>${esc(p.nick)}${p.positionName ? ' · ' + esc(p.positionName) : ''}</option>`)
    .join('');

  const statOpts = (sel) => cfg.stats
    .map((s) => `<option value="${esc(s.key)}"${s.key === sel ? ' selected' : ''}>${esc(s.label)}</option>`)
    .join('');

  const rows = calcState.slots.map((s, i) => `<div class="calc-slot">
    <span class="calc-slot-n">Ô ${i + 1}</span>
    <select data-calc="stat" data-i="${i}" aria-label="Chỉ số ô ${i + 1}">${statOpts(s.statKey)}</select>
    <select data-calc="tier" data-i="${i}" aria-label="Tier ô ${i + 1}">
      ${tiers.map(([k, v]) => `<option value="${esc(k)}"${k === s.tier ? ' selected' : ''}>Tier ${esc(k)} (+${v}%)</option>`).join('')}
    </select>
    <select data-calc="trait" data-i="${i}" aria-label="Trait ô ${i + 1}">
      ${traits.map(([k, v]) => `<option value="${esc(k)}"${k === s.trait ? ' selected' : ''}>${esc(v.label || k)}</option>`).join('')}
    </select>
  </div>`).join('');

  body.innerHTML = `
    <div class="calc-head">
      <label class="sr-only" for="calc-player">Tuyển thủ</label>
      <select id="calc-player">${playerOpts}</select>
    </div>
    <div class="calc-slots">${rows}</div>
    <div id="calc-result"></div>`;

  $('#calc-player').addEventListener('change', (e) => {
    calcState.playerId = Number(e.target.value);
    runCalc();
  });

  // 80 tuyển thủ trong một danh sách thả xuống là chỗ khó chọn nhất trang.
  enhanceSelect($('#calc-player'), 'Gõ tên tuyển thủ…');

  body.querySelectorAll('[data-calc]').forEach((el) => {
    el.addEventListener('change', (e) => {
      const i = Number(e.target.dataset.i);
      calcState.slots[i][e.target.dataset.calc] = e.target.value;
      runCalc();
    });
  });

  runCalc();
}

async function runCalc() {
  const out = $('#calc-result');
  const emblems = calcState.slots.map((s) => `${s.statKey}:${s.tier}:${s.trait}`).join(',');

  try {
    const d = await getJson(`api/fantasy/banner-score?playerId=${calcState.playerId}&emblems=${encodeURIComponent(emblems)}`);
    if (!d.ready) { out.innerHTML = `<div class="empty">${esc(d.note || '')}</div>`; return; }

    // Chênh lệch so với bản không trait là con số quyết định giữ hay quay lại, nên nó
    // phải hiện thành một con số riêng chứ không bắt người đọc tự trừ.
    const delta = Math.round((d.total - d.totalWithoutTraits) * 100) / 100;
    const sign = delta >= 0 ? '+' : '';

    out.innerHTML = `
      <div class="bento" style="margin-top:var(--s-4)">
        <article class="kpi p2">
          <div class="kpi-label">Điểm banner</div>
          <div class="kpi-value">${n0(d.total)}</div>
          <div class="kpi-note">${esc(d.nick)}${d.positionName ? ' · ' + esc(d.positionName) : ''}</div>
        </article>
        <article class="kpi p2">
          <div class="kpi-label">Trait đóng góp</div>
          <div class="kpi-value">${sign}${delta}</div>
          <div class="kpi-note">so với cùng bộ emblem không trait (${n0(d.totalWithoutTraits)})</div>
        </article>
      </div>

      <div class="table-scroll" style="margin-top:var(--s-3)"><table>
        <thead><tr>
          <th scope="col">Ô</th><th scope="col">Chỉ số</th><th scope="col">Điểm gốc</th>
          <th scope="col">Tier</th><th scope="col">Trait</th>
          <th scope="col">Hệ số trait</th><th scope="col">Tổng hệ số</th><th scope="col">Điểm</th>
        </tr></thead>
        <tbody>${d.slots.map((s) => `<tr>
          <td>${s.slot + 1}</td>
          <td>${esc(s.statLabel)}</td>
          <td class="num">${s.basePoints}</td>
          <td class="num">+${s.tierBonusPercent}%</td>
          <td>${esc(s.trait)}</td>
          <td class="num">×${s.traitFactor}</td>
          <td class="num">×${s.factor}</td>
          <td class="num"><b>${fmt(s.points, 1)}</b></td>
        </tr>`).join('')}</tbody>
      </table></div>

      ${d.tierVsTrait ? `<div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div><b>Tier ${esc(d.tierVsTrait.tierRange)} · Trait ${esc(d.tierVsTrait.traitRange)}</b><br>
        ${esc(d.tierVsTrait.verdict)}</div>
      </div>` : ''}

      ${d.slots.map((s) => altTable(s)).join('')}

      <div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.note || '')}</div>
      </div>`;
  } catch (err) {
    out.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

/**
 * Bảng thay thế cho một ô: nếu không quay ra được thứ tốt nhất thì thứ nào thay được.
 *
 * Chỉ hiện 6 dòng quanh lựa chọn hiện tại thay vì cả 30 — mục đích là trả lời "thay bằng gì",
 * không phải bày ra toàn bộ không gian rồi để người đọc tự lọc. Dòng đang dùng luôn nằm trong
 * danh sách để thấy mình đang ở đâu.
 */
function altTable(s) {
  const alts = s.alternatives || [];
  if (!alts.length) return '';

  const at = alts.findIndex((a) => a.isCurrent);
  const best = alts[0];
  const from = Math.max(0, Math.min(at - 2, alts.length - 6));
  const window = alts.slice(from, from + 6);

  return `<details style="margin-top:var(--s-3)">
    <summary><b>Ô ${s.slot + 1} · ${esc(s.statLabel)}</b> — đang là tier ${esc(s.tier)} / ${esc(s.trait)},
      xếp thứ <b>${at + 1}</b>/${alts.length}${at === 0 ? ' (tốt nhất)' : ` · kém nhất bảng ${Math.round((best.total - alts[at].total) * 100) / 100} điểm`}</summary>
    <div class="table-scroll" style="margin-top:var(--s-2)"><table>
      <thead><tr>
        <th scope="col">Tier</th><th scope="col">Trait</th>
        <th scope="col">Tổng banner</th><th scope="col">So với hiện tại</th>
      </tr></thead>
      <tbody>${window.map((a) => `<tr${a.isCurrent ? ' style="font-weight:600"' : ''}>
        <td>${esc(a.tier)} <span class="na">(+${a.tierBonusPercent}%)</span></td>
        <td>${esc(a.trait)}</td>
        <td class="num">${fmt(a.total, 1)}</td>
        <td class="num">${a.isCurrent ? '— đang dùng'
          : (a.delta > 0 ? '+' : '') + a.delta}</td>
      </tr>`).join('')}</tbody>
    </table></div>
  </details>`;
}

async function loadFantasyConfig() {
  const state = $('#fantasy-state');
  const body = $('#fantasy-config');

  try {
    const c = await getJson('api/fantasy/config');

    state.className = c.ready ? 'note' : 'note warn';
    state.innerHTML = `
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div>${c.ready
        ? `<b>Đang tính điểm.</b> Nguồn hệ số: ${esc(c.source || '—')}.`
        : `<b>Đang chờ bảng hệ số.</b> Khung đã dựng xong và sẽ tự chạy ngay khi
           <code>data/fantasy.json</code> được điền — còn thiếu
           <b>${(c.missingCoefficients || []).length}</b> hệ số. Không tính điểm khi còn thiếu,
           vì một bảng toàn số 0 trông y hệt một bảng đã đo.`}</div>`;

    const rows = (c.stats || []).map((s) => `<tr>
      <td>${esc(s.label)}</td>
      <td class="num">${s.per === 1 ? '1' : s.per}</td>
      <td class="num">${s.points === null || s.points === undefined
        ? '<span class="na">chưa điền</span>' : s.points}</td>
    </tr>`).join('');

    const slots = (c.slots || []).map((s) => `${esc(s.group)} × ${s.count}`).join(' · ');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Hệ số tính điểm fantasy</caption>
        <thead><tr><th scope="col">Chỉ số</th><th scope="col">Mỗi</th><th scope="col">Điểm</th></tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      <p class="desc" style="margin-top:var(--s-3)">Suất đội hình: <b>${esc(slots || '—')}</b>
      ${c.slotsConfirmed ? '' : ' — <b>chưa xác nhận</b> theo luật TI2026, đang là phỏng đoán.'}</p>

      <div class="note warn" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div><b>Ba thứ không nguồn nào lấy được</b>, kể cả khi đã điền hệ số:
        <br>${(c.unavailable || []).map((u) => '· ' + esc(u)).join('<br>')}</div>
      </div>`;
  } catch (err) {
    state.className = 'error';
    state.textContent = 'Không tải được cấu hình fantasy: ' + (err.serverMessage || err.message);
    body.innerHTML = '';
  }
}

/**
 * /optimize dùng chung cho ba mục con (đội hình, đối chiếu TI2025, danh hiệu). Gọi ba lần thì
 * máy chủ chấm điểm lại ba lượt cho cùng một câu trả lời, nên nhớ lại kết quả trong phiên.
 */
let optimizeOnce = null;
const getOptimize = () => (optimizeOnce ??= getJson('api/fantasy/optimize'));

/** Thẻ một tuyển thủ trong đội hình. Tên ĐỘI phải hiện, vì luật ràng buộc CẶP CÙNG ĐỘI. */
function rosterCard(p) {
  return `<article class="kpi p3">
    <div class="kpi-label">${esc(p.positionName || p.slot)}</div>
    <div class="kpi-value">${esc(p.nick)}</div>
    <div class="kpi-note">${esc(p.teamName || '—')}<br>${p.bannerPoints} điểm banner · ${p.matches} trận
      ${p.banner ? `<br><span style="font-size:.9em">${bannerSlots(p.banner)}</span>` : ''}</div>
  </article>`;
}

async function loadFantasyBaseline() {
  const body = $('#fantasy-baseline');
  body.innerHTML = '<div class="skeleton" style="height:160px"></div>';

  try {
    const d = await getOptimize();
    const b = d.baseline;
    if (!d.ready || !b) { body.innerHTML = '<div class="empty">Chưa có mốc đối chiếu.</div>'; return; }

    body.innerHTML = `
      <div class="bento">${b.roster.map(rosterCard).join('')}</div>

      ${b.comparable ? `<div class="bento" style="margin-top:var(--s-4)">
        <article class="kpi p2">
          <div class="kpi-label">Tổng điểm ${esc(b.label)}</div>
          <div class="kpi-value">${b.projectedTotal}</div>
          <div class="kpi-note">cùng thang đo với đội hình hiện tại</div>
        </article>
      </div>` : `<div class="note warn" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div><b>Chưa hiện tổng điểm — cố ý.</b><br>${esc(b.incomparableNote || '')}</div>
      </div>`}

      <div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(b.note || '')}</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

async function loadFantasyRoster() {
  const body = $('#fantasy-roster');
  body.innerHTML = '<div class="skeleton" style="height:160px"></div>';

  try {
    const d = await getOptimize();

    if (!d.ready) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa xếp được đội hình.')}</div>`;
      return;
    }

    body.innerHTML = `
      <div class="bento">${d.roster.map(rosterCard).join('')}</div>

      <div class="bento" style="margin-top:var(--s-4)">
        <article class="kpi p2">
          <div class="kpi-label">Tổng điểm dự kiến</div>
          <div class="kpi-value">${d.projectedTotal}</div>
          <div class="kpi-note">điểm gốc ${d.basePoints} + danh hiệu ${Math.round((d.projectedTotal - d.basePoints) * 100) / 100}</div>
        </article>
      </div>

      ${titleNote(d)}

      ${partialNote(d.partial)}

      ${(d.shortfall || []).length ? `<div class="note warn" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div>Chưa đủ người cho vài suất: ${d.shortfall.map((s) => esc(s)).join(' · ')}</div>
      </div>` : ''}

      <div class="note" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.method || '')}<br><br><b>${esc(d.limitation || '')}</b>
        <br><br><button class="pill" type="button" data-goto-sec="Máy tính emblem">Mở máy tính emblem →</button></div>
      </div>
      ${biasNote(d.bias)}`;
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

/**
 * Ba ô emblem của một banner, giữ đúng thứ tự màu.
 *
 * Màu phải hiện ra chứ không chỉ tên chỉ số: cả luật nằm ở chỗ ô nào ăn được màu nào, và ô
 * TRỐNG (chưa đo được chỉ số nào của màu đó) phải trông khác hẳn ô có số — nếu không thì một
 * banner mới nạp được một phần sẽ trông y hệt một banner đầy đủ nhưng điểm thấp.
 */
/**
 * Prefix tốt nhất cho đúng đội hình đang gợi ý.
 *
 * Hiện cả bảng chứ không chỉ cái tốt nhất: khoảng cách giữa hạng nhất và hạng nhì mới cho biết
 * lựa chọn này có chắc hay không. Chênh 2 điểm thì chọn cái nào cũng như nhau.
 */
/**
 * Danh hiệu đã được chọn CÙNG LÚC với đội hình, nên hiện nó cạnh đội hình chứ không phải như
 * một bảng tra cứu rời. Người đọc cần thấy ngay: đội hình này đi với danh hiệu nào, và danh
 * hiệu đó đóng góp bao nhiêu điểm.
 */
function titleNote(d) {
  const cell = (t, kind) => t ? `<article class="kpi p3">
      <div class="kpi-label">${kind}</div>
      <div class="kpi-value">${esc(t.label || t.key)}</div>
      <div class="kpi-note">+${t.bonusPercent}% khi ${esc(t.condition || 'thoả điều kiện')}<br>
        <b>+${t.expectedPoints}</b> điểm kỳ vọng cho đội hình này</div>
    </article>` : `<article class="kpi p3">
      <div class="kpi-label">${kind}</div>
      <div class="kpi-value"><span class="na">chưa đủ dữ liệu</span></div>
    </article>`;

  if (!d.prefix && !d.suffix) return '';

  return `<h3 style="margin-top:var(--s-6)">Danh hiệu đi kèm đội hình này</h3>
    <div class="bento">${cell(d.prefix, 'Prefix')}${cell(d.suffix, 'Suffix')}</div>
    <div class="note" style="margin-top:var(--s-3)">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
      <div>${esc(d.titleNote || '')}</div>
    </div>`;
}

/** Cảnh báo xếp hạng chưa ổn định trong lúc nạp bù — xem PartialWarning phía máy chủ. */
function partialNote(p) {
  if (!p || !p.count) return '';
  const list = p.stats.map((s) => `${esc(s.label)} <b>${s.percent}%</b>`).join(' · ');

  return `<div class="note warn" style="margin-top:var(--s-4)">
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
    <div><b>Đang nạp bù — thứ hạng chưa ổn định.</b><br>${list}<br><br>${esc(p.message)}</div>
  </div>`;
}

function bannerSlots(b) {
  if (!b) return '<span class="na">—</span>';

  const tone = { red: '#e5534b', blue: '#4c8dff', green: '#3fb950' };
  const dot = (c) => `<span aria-hidden="true" style="display:inline-block;width:.55em;height:.55em;
    border-radius:50%;background:${tone[c] || 'currentColor'};margin-right:.35em"></span>`;

  const filled = b.slots.map((s) =>
    `<span class="chip" title="${esc(s.color)} · ${s.points} điểm">${dot(s.color)}${esc(s.statLabel)}</span>`);

  const empty = (b.emptySlots || []).map((c) =>
    `<span class="chip na" title="chưa đo được chỉ số nào màu này">${dot(c)}trống</span>`);

  return filled.concat(empty).join(' ');
}

async function loadFantasyPlayers() {
  const body = $('#fantasy-players');
  body.innerHTML = '<div class="skeleton" style="height:220px"></div>';

  try {
    const d = await getJson('api/fantasy/players');

    if (!d.ready || !d.players?.length) {
      body.innerHTML = `<div class="empty">${esc(d.note || 'Chưa có dữ liệu.')}</div>`;
      return;
    }

    // Cột phân rã dựng từ chính danh sách chỉ số API trả về, không hardcode —
    // thêm một chỉ số vào fantasy.json là bảng tự có thêm cột.
    const keys = d.players[0].parts.map((p) => ({ key: p.key, label: p.label }));

    const head = keys.map((k) => `<th scope="col">${esc(k.label)}</th>`).join('');

    const rows = d.players.map((p) => `<tr>
      <td>${esc(p.nick)}</td>
      <td>${esc(p.positionName || '—')}</td>
      <td class="num"><b>${p.banner ? p.banner.basePoints : '<span class="na">—</span>'}</b></td>
      <td class="num">${p.banner ? `${p.banner.tierIPoints} – ${p.banner.tierVPoints}` : '<span class="na">—</span>'}</td>
      <td>${bannerSlots(p.banner)}</td>
      <td class="num">${p.allStatsTotal}</td>
      <td class="num">${p.matches}</td>
      ${p.parts.map((x) => `<td class="num">${x.avgPoints === null || x.avgPoints === undefined
        ? '<span class="na">—</span>' : x.avgPoints}</td>`).join('')}
    </tr>`).join('');

    body.innerHTML = `
      <div class="table-scroll"><table>
        <caption class="sr-only">Điểm fantasy trung bình mỗi trận của từng tuyển thủ</caption>
        <thead><tr>
          <th scope="col">Tuyển thủ</th><th scope="col">Vị trí</th>
          <th scope="col">Điểm banner</th><th scope="col">Sàn – trần tier</th>
          <th scope="col">Ba ô emblem</th>
          <th scope="col">Tổng 18 chỉ số</th><th scope="col">Trận</th>${head}
        </tr></thead>
        <tbody>${rows}</tbody>
      </table></div>

      ${partialNote(d.partial)}

      <div class="note warn" style="margin-top:var(--s-4)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3l9 16H3z"/><path d="M12 9v4M12 16.5v.01"/></svg>
        <div>${esc(d.bannerNote || '')}</div>
      </div>

      <div class="note" style="margin-top:var(--s-3)">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 8v5M12 16.5v.01"/></svg>
        <div>${esc(d.method || '')}<br><br>${esc(d.caveat || '')}</div>
      </div>`;
  } catch (err) {
    body.innerHTML = `<div class="error">${esc(err.serverMessage || err.message)}</div>`;
  }
}

/* ============================ Sắp xếp bảng ============================ */

/**
 * Đọc một ô thành giá trị so sánh được.
 *
 * Trả { empty } cho ô trống hoặc "—". Ô KHÔNG BIẾT không phải ô nhỏ nhất: xếp "chưa đo được"
 * lên đầu khi sắp giảm dần sẽ đẩy đúng những hàng vô nghĩa lên chỗ dễ thấy nhất.
 */
function sortCellValue(td) {
  const raw = (td?.textContent || '').trim();
  if (!raw || raw === '—') return { empty: true };

  // mm:ss — mốc thời gian phải so theo giây, không so theo chuỗi ("9:30" > "12:20" nếu so chuỗi)
  const clock = raw.match(/^(\d+):([0-5]\d)$/);
  if (clock) return { num: Number(clock[1]) * 60 + Number(clock[2]) };

  // Bỏ mọi thứ không phải số, dấu phân cách hay dấu âm. Giữ cả '−' (U+2212) vì đó là dấu trừ
  // thật trong văn bản tiếng Việt, khác với '-' của bàn phím.
  let s = raw.replace(/[+%\s]/g, '').replace(/−/g, '-');

  // "1.234" trong tiếng Việt là một nghìn hai trăm ba tư, không phải 1.234. Chỉ coi dấu chấm
  // là phân cách nghìn khi nó khớp đúng khuôn nhóm ba chữ số — mọi trường hợp khác giữ nguyên
  // vì phần lớn số trên trang này do JS sinh ra với dấu chấm thập phân.
  if (/^-?\d{1,3}(\.\d{3})+$/.test(s)) s = s.replace(/\./g, '');

  const num = Number(s);
  if (s !== '' && Number.isFinite(num)) return { num };

  return { text: raw.toLocaleLowerCase('vi') };
}

/** Cột chỉ đáng sắp xếp khi có ít nhất một ô đọc được — cột chỉ chứa thanh vẽ thì không. */
function columnSortable(rows, index) {
  return rows.some((tr) => {
    const v = sortCellValue(tr.cells[index]);
    return !v.empty;
  });
}

function sortTable(table, index, asc) {
  const tbody = table.tBodies[0];
  if (!tbody) return;

  const rows = [...tbody.rows];

  rows.sort((a, b) => {
    const x = sortCellValue(a.cells[index]);
    const y = sortCellValue(b.cells[index]);

    // Ô không biết luôn nằm cuối, BẤT KỂ chiều sắp xếp
    if (x.empty && y.empty) return 0;
    if (x.empty) return 1;
    if (y.empty) return -1;

    if ('num' in x && 'num' in y) return asc ? x.num - y.num : y.num - x.num;

    const cmp = String(x.text ?? x.num).localeCompare(String(y.text ?? y.num), 'vi');
    return asc ? cmp : -cmp;
  });

  rows.forEach((tr) => tbody.appendChild(tr));
}

function markSortableHeaders(table) {
  const tbody = table.tBodies[0];
  const rows = tbody ? [...tbody.rows] : [];
  if (rows.length < 2) return;

  [...(table.tHead?.rows[0]?.cells || [])].forEach((th, i) => {
    if (th.dataset.sortReady) return;
    if (!columnSortable(rows, i)) return;

    th.dataset.sortReady = '1';
    th.dataset.index = String(i);
    th.tabIndex = 0;
    th.setAttribute('role', 'button');
    th.title = 'Bấm để sắp xếp';
    if (!$('.sort', th)) th.insertAdjacentHTML('beforeend', '<span class="sort" aria-hidden="true">↕</span>');
  });
}

function applySort(th) {
  const table = th.closest('table');
  const index = Number(th.dataset.index);

  // Bấm lại cùng cột thì đảo chiều; sang cột mới thì bắt đầu bằng GIẢM DẦN, vì với bảng số
  // liệu câu hỏi đầu tiên gần như luôn là "cái nào lớn nhất".
  const asc = th.getAttribute('aria-sort') === 'descending';

  [...(table.tHead?.rows[0]?.cells || [])].forEach((other) => {
    other.removeAttribute('aria-sort');
    const icon = $('.sort', other);
    if (icon) icon.textContent = '↕';
  });

  th.setAttribute('aria-sort', asc ? 'ascending' : 'descending');
  const icon = $('.sort', th);
  if (icon) icon.textContent = asc ? '↑' : '↓';

  sortTable(table, index, asc);
}

/**
 * Uỷ quyền ở cấp document thay vì gắn vào từng bảng.
 *
 * Chín bảng trong trang được vẽ bằng chuỗi HTML ở chín chỗ khác nhau, và còn bảng sẽ thêm về
 * sau. Gắn tay thì mỗi lần thêm bảng lại phải nhớ gọi — và lần quên đầu tiên sẽ là một bảng
 * im lặng không sắp xếp được. Uỷ quyền thì bảng mới tự có, không cần biết gì.
 *
 * Bảng nào tự lo việc sắp xếp (bảng chỉ số render lại từ mô hình dữ liệu) thì khai
 * data-sort="custom" để đứng ngoài.
 */
function setupTableSorting() {
  const handle = (th) => {
    if (!th || th.closest('table[data-sort="custom"]')) return;
    if (!th.dataset.sortReady) markSortableHeaders(th.closest('table'));
    if (th.dataset.sortReady) applySort(th);
  };

  document.addEventListener('click', (e) => {
    handle(e.target.closest('thead th'));
  });

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return;
    const th = e.target.closest?.('thead th');
    if (!th || th.closest('table[data-sort="custom"]')) return;
    e.preventDefault();
    handle(th);
  });

  // Gắn dấu hiệu bấm được cho mọi bảng đã có và mọi bảng vẽ thêm sau này
  const mark = () => $$('table:not([data-sort="custom"])').forEach(markSortableHeaders);
  new MutationObserver(mark).observe($('#main'), { childList: true, subtree: true });
  mark();
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

  // Cho phép nhảy sang một mục con bằng data-goto-sec. Cần vì có những chỗ nội dung phải chỉ
  // sang mục khác — ví dụ trang gợi ý đội hình nói "muốn tính bộ emblem vừa quay thì xem máy
  // tính emblem". Bảo người đọc tự đi tìm cái tab đó là đúng thứ ta vẫn đang tránh.
  document.addEventListener('click', (e) => {
    const target = e.target.closest?.('[data-goto-sec]');
    if (!target) return;

    const name = target.dataset.gotoSec;
    const btn = [...document.querySelectorAll('.subpill')].find((b) => b.textContent === name);
    if (btn) btn.click();
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
      if (view === 'fantasy' && !loadedViews.has('fantasy')) {
        loadedViews.add('fantasy');
        loadFantasy();
      }

      if (view === 'tiers' && !loadedViews.has('tiers')) {
        loadedViews.add('tiers');
        setupTierList();
      }

      // Lịch thì nạp LẠI mỗi lần mở, không nhớ như các tab kia: nó có đồng hồ đếm ngược và
      // trạng thái đang-diễn-ra, mà dữ liệu cũ từ nửa tiếng trước thì hai thứ đó đều sai.
      if (view === 'schedule') loadSchedule();
      if (view === 'profile') setupProfile();

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
setupTableSorting();
boot();
