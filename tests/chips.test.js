const assert = require('node:assert/strict');
global.window = {};
global.Node = { TEXT_NODE: 3, ELEMENT_NODE: 1 };
require('../src/AtomixAI.UI/wwwroot/js/chips.js');

const text = value => ({ nodeType: 3, textContent: value });
const element = (tagName, children = [], id = null) => ({
    nodeType: 1, tagName, childNodes: children,
    classList: { contains: name => name === 'bim-chip' && id !== null },
    getAttribute: name => name === 'data-chip-id' ? id : null
});
const chip = id => element('SPAN', [text('#Same display name')], id);
const serialize = root => window.Atomix.ChipsController.serialize(root);

const messageChip = {
    tagName: 'SPAN',
    attributes: { 'data-chip-id': 'chip_1' },
    contentEditable: 'true',
    classList: { values: new Set(), add(name) { this.values.add(name); } },
    getAttribute(name) { return this.attributes[name] || null; },
    setAttribute(name, value) { this.attributes[name] = value; }
};
assert.equal(window.Atomix.ChipsController.restoreMessageTooltip(messageChip, new Map([
    ['chip_1', { chipTooltip: 'Instance parameter\nParameter: Height = 12 mm' }]
])), true);
assert.equal(messageChip.attributes['data-tooltip'], 'Instance parameter\nParameter: Height = 12 mm');
assert.equal(messageChip.contentEditable, 'false');
assert.equal(messageChip.classList.values.has('bim-chip'), true);
assert.equal(window.Atomix.ChipsController.restoreMessageTooltip(messageChip, new Map()), false);

assert.deepEqual(serialize(element('DIV', [
    text('Set '), chip('chip_1'), text(' to '), chip('chip_2'), text(' using '), chip('chip_1')
])), { prompt: 'Set [chip_1] to [chip_2] using [chip_1]', chipIds: ['chip_1', 'chip_2'] });
assert.deepEqual(serialize(element('DIV', [
    text('first'), element('DIV', [text('second'), element('BR'), text('third')]),
    element('P', [text('fourth')])
])), { prompt: 'first\nsecond\nthird\nfourth', chipIds: [] });
assert.deepEqual(serialize(element('DIV', [text('Only plain #tag and [chip_1] text')])), {
    prompt: 'Only plain #tag and [chip_1] text', chipIds: []
});
assert.throws(() => serialize(element('DIV', [chip('')])), /Stale chip/);
assert.throws(() => serialize(element('DIV', [chip('chip_0')])), /Stale chip/);
assert.deepEqual(serialize(element('DIV', [])), { prompt: '', chipIds: [] });
console.log('Chip DOM serialization tests passed.');
