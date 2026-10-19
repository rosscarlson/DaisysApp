// iRacing full history export
// HOW TO USE:
//   1. Log in at https://members-ng.iracing.com in Chrome/Edge/Firefox.
//   2. Press F12 -> Console tab (type "allow pasting" first if the browser asks).
//   3. Paste this whole file, press Enter. Leave the tab open until it finishes
//      (10-30 min depending on history; progress prints in the console).
//   4. Four files download: races_all.csv, irating_history.csv, sr_history.csv, iracing_raw.json
// Uses only your own logged-in session to read your own data. Changes nothing.

(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const log = (...a) => console.log('%c[iR export]', 'color:#0bf;font-weight:bold', ...a);

  // Fetch a /data endpoint through the site's own proxy, follow the S3 "link", retry on rate limits.
  async function ir(path, params = {}) {
    const url = '/bff/pub/proxy/data/' + path + '?' + new URLSearchParams(params);
    for (let attempt = 0; attempt < 8; attempt++) {
      try {
        const r = await fetch(url, { credentials: 'include' });
        if (r.status === 429) { await sleep(10000 * (attempt + 1)); continue; }
        if (!r.ok) throw new Error(r.status + ' ' + (await r.text()).slice(0, 200));
        const j = await r.json();
        if (j.link) return await (await fetch(j.link)).json();
        return j;
      } catch (e) {
        if (attempt === 7) throw e;
        await sleep(2000 * (attempt + 1));
      }
    }
  }

  async function pool(items, n, fn) {
    let i = 0;
    const worker = async () => { while (i < items.length) { const idx = i++; await fn(items[idx], idx); } };
    await Promise.all(Array.from({ length: n }, worker));
  }

  // 1. Member info
  const me = await ir('member/info');
  const custId = me.cust_id;
  log(`Logged in as ${me.display_name} (cust_id ${custId}), member since ${me.member_since}`);

  // 2. Search series + hosted results in 90-day windows
  const windows = [];
  const end = new Date();
  for (let s = new Date(me.member_since + 'T00:00:00Z'); s < end;) {
    const e = new Date(Math.min(s.getTime() + 90 * 864e5, end.getTime()));
    windows.push([s.toISOString().slice(0, 19) + 'Z', e.toISOString().slice(0, 19) + 'Z']);
    s = e;
  }
  const rows = { series: [], hosted: [] };
  const tasks = [];
  for (const w of windows) for (const kind of ['series', 'hosted']) tasks.push([kind, w]);
  let done = 0;
  await pool(tasks, 3, async ([kind, w]) => {
    try {
      const j = await ir('results/search_' + kind, { cust_id: custId, start_range_begin: w[0], start_range_end: w[1] });
      const ci = j.data && j.data.chunk_info;
      if (ci && ci.chunk_file_names) {
        for (const f of ci.chunk_file_names) rows[kind].push(...await (await fetch(ci.base_download_url + f)).json());
      }
    } catch (e) { console.warn('search failed', kind, w, e); }
    if (++done % 20 === 0) log(`search ${done}/${tasks.length}`);
  });
  log(`Found ${rows.series.length} official sessions, ${rows.hosted.length} hosted sessions`);

  // 3. iRating / SR before & after for official races and qualifying
  const ratingIds = rows.series
    .filter(r => r.event_type_name === 'Race' || r.event_type_name === 'Qualify')
    .map(r => r.subsession_id);
  const ratings = {};
  const findMe = arr => {
    for (const x of arr || []) {
      if (x.cust_id === custId) return x;
      if (x.driver_results) { const d = x.driver_results.find(d => d.cust_id === custId); if (d) return d; }
    }
  };
  done = 0;
  await pool(ratingIds, 2, async id => {
    try {
      const j = await ir('results/get', { subsession_id: id, include_licenses: false });
      const ss = j.session_results || [];
      const main = ss.find(s => s.simsession_number === 0) || ss[ss.length - 1];
      let m = main && findMe(main.results);
      if (!m) for (const s of ss) { m = findMe(s.results); if (m) break; }
      if (m) ratings[id] = { oi: m.oldi_rating, ni: m.newi_rating, os: m.old_sub_level, ns: m.new_sub_level, ol: m.old_license_level, nl: m.new_license_level };
    } catch (e) { console.warn('results/get failed', id, e); }
    if (++done % 50 === 0) log(`ratings ${done}/${ratingIds.length}`);
  });

  // 4. iRating (chart_type 1) and SR (chart_type 3) history, categories 1-6
  const charts = {};
  for (const ct of [1, 3]) for (let c = 1; c <= 6; c++) {
    try { charts[ct + '_' + c] = (await ir('member/chart_data', { cust_id: custId, chart_type: ct, category_id: c })).data || []; }
    catch (e) { charts[ct + '_' + c] = []; console.warn('chart failed', ct, c, e); }
  }

  // 5. Build CSVs
  const q = v => { if (v === undefined || v === null) return ''; v = String(v); return /[",\n]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v; };
  const neg = v => (v == null || v < 0) ? '' : v;
  const pos = v => (v == null || v < 0) ? '' : v + 1; // API positions are 0-based
  const licLetter = l => (l == null || l < 0) ? '' : (l <= 4 ? 'R' : l <= 8 ? 'D' : l <= 12 ? 'C' : l <= 16 ? 'B' : l <= 20 ? 'A' : 'P');
  const sr = (l, s) => (s == null || s < 0) ? '' : licLetter(l) + ' ' + (s / 100).toFixed(2);

  const header = 'date,session_type,series,track,config,car,car_class,license_category,sof,start_pos,start_pos_class,finish_pos,finish_pos_class,laps_complete,laps_led,incidents,champ_points,irating_before,irating_after,irating_change,sr_before,sr_after,subsession_id,official,season_year,season_quarter,race_week,num_drivers';
  const all = [...rows.series, ...rows.hosted].sort((a, b) => (a.start_time < b.start_time ? -1 : 1));
  const raceLines = all.map(r => {
    const x = ratings[r.subsession_id] || {};
    const car = r.car_name || (r.cars ? r.cars.map(c => c.car_name).join('; ') : '');
    return [
      r.start_time, r.event_type_name || 'Hosted', r.series_name || r.session_name || '',
      r.track && r.track.track_name, r.track && r.track.config_name, car, r.car_class_name || '',
      r.license_category || '', neg(r.event_strength_of_field),
      pos(r.starting_position), pos(r.starting_position_in_class), pos(r.finish_position), pos(r.finish_position_in_class),
      neg(r.laps_complete), neg(r.laps_led), neg(r.incidents), neg(r.champ_points),
      neg(x.oi), neg(x.ni), (x.oi > 0 && x.ni > 0) ? x.ni - x.oi : '',
      sr(x.ol, x.os), sr(x.nl, x.ns), r.subsession_id, r.official_session,
      r.season_year, r.season_quarter, r.race_week_num == null ? '' : r.race_week_num + 1, r.num_drivers
    ].map(q).join(',');
  });

  const cat = { 1: 'Oval', 2: 'Road', 3: 'Dirt Oval', 4: 'Dirt Road', 5: 'Sports Car', 6: 'Formula' };
  const cls = ['', 'R', 'D', 'C', 'B', 'A', 'P'];
  const ih = [], sh = [];
  for (let c = 1; c <= 6; c++) {
    charts['1_' + c].forEach(d => ih.push([d.when, cat[c], d.value].map(q).join(',')));
    charts['3_' + c].forEach(d => sh.push([d.when, cat[c], cls[Math.floor(d.value / 1000)] || '', ((d.value % 1000) / 100).toFixed(2), d.value].map(q).join(',')));
  }

  const files = {
    'races_all.csv': [header, ...raceLines].join('\n'),
    'irating_history.csv': ['date,category,irating', ...ih.sort()].join('\n'),
    'sr_history.csv': ['date,category,license_class,safety_rating,raw_value', ...sh.sort()].join('\n'),
    'iracing_raw.json': JSON.stringify({ member: { cust_id: custId, name: me.display_name, member_since: me.member_since }, series_sessions: rows.series, hosted_sessions: rows.hosted, per_race_ratings: ratings, chart_data: charts }, null, 1)
  };

  // 6. Download
  for (const [name, content] of Object.entries(files)) {
    const isCsv = name.endsWith('.csv');
    const blob = new Blob([isCsv ? '\ufeff' + content : content], { type: isCsv ? 'text/csv' : 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a); a.click(); a.remove();
    await sleep(700);
  }
  log(`Done: ${all.length} sessions exported. Check your Downloads folder.`);
})();
