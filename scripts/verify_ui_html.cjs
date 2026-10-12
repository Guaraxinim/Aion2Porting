const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const html = fs.readFileSync('design/Aion2Porting-UI.html', 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
new vm.Script(script);
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(x => x[1]);
assert.equal(new Set(ids).size, ids.length, 'Duplicate IDs');
assert.equal(/(?:src|href)="https?:/.test(html), false, 'External dependency');
assert.equal(/__(LOGO|FONT|ICON)__/.test(html), false, 'Unresolved embedded asset');
for (const [, id] of script.matchAll(/\$\('([^']+)'\)/g)) {
    assert.ok(ids.includes(id) || script.includes('id="' + id + '"'), 'Missing element: ' + id);
}
console.log('Passed: JavaScript syntax, element references, unique IDs, embedded assets and offline dependencies.');
