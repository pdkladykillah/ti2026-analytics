/* ============================================================================
   TI 2026 — logic giao diện
   ----------------------------------------------------------------------------
   Không framework, không build step. Mọi đường dẫn API là TƯƠNG ĐỐI để trang
   chạy đúng cả khi đứng sau reverse proxy với prefix (/ti2026).
   ========================================================================= */

'use strict';

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

const HERO_CDN = 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/heroes/';

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
  if (!res.ok) throw new Error(`${path} trả về HTTP ${res.status}`);
  return res.json();
}

async function boot() {
  try {
    const teamsDoc = await getJson('api/teams');
    state.teams = teamsDoc.teams || [];

    // Các nguồn phụ được phép hỏng mà không kéo cả trang xuống: thiếu tier list
    // thì chỉ tab tier list trống, không phải trang trắng.
    const [meta, rosters, h2h, tiers] = await Promise.all([
      getJson('api/meta').catch(() => ({})),
      getJson('api/rosters').then((d) => d.rosters || {}).catch(() => ({})),
      getJson('api/h2h').catch(() => null),
      getJson('api/tiers').catch(() => null),
    ]);

    state.meta = meta;
    state.rosters = rosters;
    state.h2h = h2h;
    state.tiers = tiers;

    renderStatus();
    renderOverview();
    renderTable();
    renderTeams();
    setupH2h();
    setupTiers();
    setupForm();
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

  grid.innerHTML = KPIS.map((k) => {
    const t = [...data].sort(k.pick)[0];
    return `<article class="kpi ${k.cls}">
      <div class="kpi-label">${esc(k.label)}</div>
      <div class="kpi-value">${k.value(t)}</div>
      <div class="kpi-team">${esc(t.name)}</div>
      <div class="kpi-note">${esc(k.note(t))}</div>
    </article>`;
  }).join('');

  const sorted = [...data].sort((a, b) => b.stats.winrate - a.stats.winrate);
  const max = sorted[0].stats.winrate || 1;

  ranking.innerHTML = sorted.map((t, i) => {
    const wr = t.stats.winrate;
    const pct = Math.max((wr / max) * 100, 3);
    const tier = tierOf(wr);
    return `<div class="h2h-row" style="grid-template-columns:28px 1fr 74px">
      <span class="num" style="color:var(--muted);font-size:12.5px">${i + 1}</span>
      <div class="h2h-bar-wrap">
        ${teamLogo(t)}
        <span style="font-weight:600;font-size:13.5px;min-width:120px">${esc(t.name)}</span>
        <span class="h2h-bar a" style="width:${pct}%;max-width:calc(100% - 170px)"></span>
      </div>
      <span class="num" style="text-align:right;font-weight:600">
        <span class="tier-badge" style="background:${TIER_COLOR[tier]}">${tier}</span>
        ${fmt(wr, 0, '%')}
      </span>
    </div>`;
  }).join('');
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

/* ============================ Tab & theme ============================ */

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
boot();
