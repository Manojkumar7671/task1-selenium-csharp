// Semantic field locator. Runs inside the page (via Selenium ExecuteScript).
// args: kind ('text' | 'radio' | 'select'), cfg {include:[], exclude:[], target:''}
// Finds the best candidate by MEANING (label text, aria, placeholder, nearby text,
// name/id tokens, option values) - never by a fixed id/xpath/css.
// Marks the winner with data-ax-pick="<kind>" and returns a JSON string.
const kind = arguments[0];
const cfg = arguments[1] || {};
const include = cfg.include || [];
const exclude = cfg.exclude || [];
const target = cfg.target || '';

document.querySelectorAll('[data-ax-pick="' + kind + '"]').forEach(e => e.removeAttribute('data-ax-pick'));

const norm = s => (s || '').replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const compact = s => norm(s).replace(/ /g, '');
const hasWord = (text, w) => (' ' + norm(text) + ' ').includes(' ' + norm(w) + ' ');
const hasPhrase = (text, p) => hasWord(text, p) || (norm(p).includes(' ') && compact(text).includes(compact(p)));
const exact = (text, p) => norm(text) === norm(p) || compact(text) === compact(p);
const txt = n => (n && (n.textContent || '')).replace(/\s+/g, ' ').trim();
const CONTROLS = 'input:not([type=hidden]),select,textarea,button';

function visible(el) {
  const st = getComputedStyle(el);
  if (st.display === 'none' || st.visibility === 'hidden') return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
}

// Text explicitly tied to the control.
function directTexts(el) {
  const out = [];
  if (el.labels) el.labels.forEach(l => out.push(txt(l)));
  if (!out.length && el.id) {
    const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]');
    if (l) out.push(txt(l));
  }
  ['aria-label', 'placeholder', 'title'].forEach(a => el.getAttribute(a) && out.push(el.getAttribute(a)));
  const lb = el.getAttribute('aria-labelledby');
  if (lb) lb.split(/\s+/).forEach(id => { const n = document.getElementById(id); if (n) out.push(txt(n)); });
  return out.filter(Boolean);
}

// Text sitting just before the control (label-less layouts, tables, divs).
function nearTexts(el) {
  const out = [];
  let node = el;
  for (let depth = 0; depth < 4 && node && node !== document.body; depth++, node = node.parentElement) {
    let p = node.previousSibling, hops = 0;
    while (p && hops < 3) {
      if (p.nodeType === 3 && p.textContent.trim()) { out.push(p.textContent.trim()); break; }
      if (p.nodeType === 1 && !p.matches(CONTROLS) && !p.querySelector(CONTROLS) && txt(p)) { out.push(txt(p)); break; }
      p = p.previousSibling; hops++;
    }
    if (out.length) break;
  }
  return out.slice(0, 2);
}

function attrTexts(el) {
  return ['name', 'id', 'autocomplete', 'data-testid', 'data-test', 'formcontrolname', 'aria-describedby']
    .map(a => el.getAttribute(a)).filter(Boolean);
}

// Score how well texts match the wanted meaning.
function scoreTexts(direct, near, attrs) {
  let best = 0, cats = 0;
  const cat = (arr, ex, co) => {
    let b = 0;
    for (const t of arr) for (const p of include) {
      if (exact(t, p)) b = Math.max(b, ex); else if (hasPhrase(t, p)) b = Math.max(b, co);
    }
    if (b) cats++;
    best = Math.max(best, b);
  };
  cat(direct, 12, 9); cat(near, 8, 7); cat(attrs, 5, 5);
  let s = best + Math.max(0, cats - 1);
  const bad = (arr, pen) => { for (const t of arr) for (const x of exclude) if (hasWord(t, x)) return pen; return 0; };
  s -= Math.max(bad(direct, 20), bad(near, 20), bad(attrs, 8));
  return s;
}

const cands = [];
const add = (el, score, desc) => cands.push({ el, score, desc });

if (kind === 'text') {
  document.querySelectorAll('input').forEach(el => {
    const t = (el.getAttribute('type') || 'text').toLowerCase();
    if (!['text', 'search', ''].includes(t) || el.disabled || el.readOnly || !visible(el)) return;
    const d = directTexts(el), n = nearTexts(el), a = attrTexts(el);
    add(el, scoreTexts(d, n, a), 'input label=' + JSON.stringify(d.concat(n)) + ' attrs=' + JSON.stringify(a));
  });
} else if (kind === 'select') {
  document.querySelectorAll('select').forEach(el => {
    if (el.disabled || !visible(el)) return;
    const d = directTexts(el), n = nearTexts(el), a = attrTexts(el);
    let s = scoreTexts(d, n, a);
    if (target && [...el.options].some(o => exact(o.text, target) || exact(o.value, target))) s += 3;
    add(el, s, 'select label=' + JSON.stringify(d.concat(n)) + ' attrs=' + JSON.stringify(a) + ' options=' + el.options.length);
  });
} else if (kind === 'radio') {
  const radios = [...document.querySelectorAll('input[type=radio]')].filter(r => !r.disabled);
  const groups = new Map();
  radios.forEach((r, i) => {
    const key = r.name || ('__solo' + i);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(r);
  });
  const optionTexts = r => {
    const out = directTexts(r);
    if (r.nextSibling && r.nextSibling.nodeType === 3) out.push(r.nextSibling.textContent.trim());
    if (r.nextElementSibling && !r.nextElementSibling.matches(CONTROLS)) out.push(txt(r.nextElementSibling));
    const par = r.parentElement;
    if (par && par.querySelectorAll('input[type=radio]').length === 1) out.push(txt(par));
    if (r.value) out.push(r.value);
    return out.filter(Boolean);
  };
  groups.forEach((members, key) => {
    // smallest ancestor containing the whole group and no foreign radios
    let c = members[0].parentElement;
    while (c && c !== document.body && !members.every(m => c.contains(m))) c = c.parentElement;
    while (c && c.parentElement && c.parentElement !== document.body &&
      [...c.parentElement.querySelectorAll('input[type=radio]')].every(x => members.includes(x)) &&
      c.parentElement.querySelectorAll(CONTROLS).length === members.length) c = c.parentElement;
    const legend = c && c.closest('fieldset') && c.closest('fieldset').querySelector('legend');
    const direct = [];
    if (legend) direct.push(txt(legend));
    ['aria-label'].forEach(a => c && c.getAttribute(a) && direct.push(c.getAttribute(a)));
    const near = c ? nearTexts(c) : [];
    const own = c ? txt(c) : '';
    const gAttrs = members.map(m => m.name || m.id).filter(Boolean);
    let gs = scoreTexts(direct, near, gAttrs);
    // container that holds the label word plus the options, e.g. "Gender Male Female Other"
    if (c && own.length < 160 && include.some(p => hasPhrase(own, p))) gs = Math.max(gs, 9);
    members.forEach(r => {
      const ot = optionTexts(r);
      let os = 0;
      for (const t of ot) {
        if (exact(t, target)) os = Math.max(os, 10);
        else if (hasWord(t, target) && !exclude.some(x => hasWord(t, x))) os = Math.max(os, 6);
      }
      if (r.value && exact(r.value, target)) os = Math.max(os, 8);
      add(r, gs + os, 'radio group=' + key + ' groupScore=' + gs + ' option=' + JSON.stringify(ot));
    });
  });
}

cands.sort((a, b) => b.score - a.score);
const best = cands[0];
const info = { kind, found: false, score: best ? best.score : null, why: best ? best.desc : 'no candidates',
  candidates: cands.slice(0, 3).map(c => ({ score: c.score, desc: c.desc })) };
const min = kind === 'radio' ? 15 : kind === 'select' ? 5 : 5;
if (best && best.score >= min) { best.el.setAttribute('data-ax-pick', kind); info.found = true; }
return JSON.stringify(info);
