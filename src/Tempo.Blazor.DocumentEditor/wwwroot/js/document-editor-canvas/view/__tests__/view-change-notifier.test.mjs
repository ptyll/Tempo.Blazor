import test from 'node:test';
import assert from 'node:assert/strict';
import { createViewChangeNotifier, viewChangeKey, VIEW_CHANGE_DEBOUNCE_MS } from '../view-change-notifier.mjs';

function createTimers() {
    let now = 0;
    let nextId = 1;
    const pending = new Map();
    return {
        setTimeout: (callback, delay) => {
            const id = nextId++;
            pending.set(id, { callback, at: now + delay });
            return id;
        },
        clearTimeout: id => pending.delete(id),
        advance(ms) {
            now += ms;
            for (const [id, timer] of [...pending]) {
                if (timer.at <= now) {
                    pending.delete(id);
                    timer.callback();
                }
            }
        },
        get pending() {
            return pending.size;
        },
    };
}

test('the key reduces a zoom to what the host renders', () => {
    assert.equal(viewChangeKey({ percent: 87, preset: 'fitWidth', autoFit: 'narrow', scale: 0.87 }), '87|fitWidth|narrow');
    assert.equal(viewChangeKey(null), '0||');
});

test('a 20 step resize (one fit per frame) produces ONE .NET notification, after the pause', () => {
    const timers = createTimers();
    let zoom = { percent: 100, preset: 'custom', autoFit: 'narrow' };
    const sent = [];
    const notifier = createViewChangeNotifier({ ...timers, read: () => zoom, notify: value => sent.push(value), initial: zoom });

    for (let step = 1; step <= 20; step += 1) {
        zoom = { percent: 100 - step, preset: 'fitWidth', autoFit: 'narrow' };
        notifier.request();
        timers.advance(16);
    }

    assert.equal(sent.length, 0, 'nothing is sent while the resize goes on');
    timers.advance(VIEW_CHANGE_DEBOUNCE_MS);
    assert.equal(sent.length, 1);
    assert.equal(sent[0].percent, 80, 'the zoom it settled on');
});

test('an unchanged percent / preset / autoFit is not sent again', () => {
    const timers = createTimers();
    const zoom = { percent: 87, preset: 'fitWidth', autoFit: 'narrow' };
    const sent = [];
    const notifier = createViewChangeNotifier({ ...timers, read: () => zoom, notify: value => sent.push(value), initial: zoom });
    notifier.request();
    timers.advance(200);
    assert.equal(sent.length, 0, 'the host already knows this state');

    notifier.markNotified({ percent: 60, preset: 'fitWidth', autoFit: 'narrow' });
    notifier.request();
    timers.advance(200);
    assert.equal(sent.length, 1, 'a state the host was not told about is sent');
    notifier.request();
    timers.advance(200);
    assert.equal(sent.length, 1);
});

test('destroy cancels the pending notification', () => {
    const timers = createTimers();
    const sent = [];
    const notifier = createViewChangeNotifier({ ...timers, read: () => ({ percent: 50 }), notify: value => sent.push(value), initial: { percent: 100 } });
    notifier.request();
    assert.equal(timers.pending, 1);
    notifier.destroy();
    assert.equal(timers.pending, 0);
    notifier.request();
    timers.advance(500);
    assert.deepEqual(sent, []);
});
