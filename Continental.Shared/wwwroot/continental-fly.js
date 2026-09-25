const DURATION = 430;
const FLIP_DURATION = 520;

const EASE = 'cubic-bezier(0.22, 0.75, 0.2, 1)';

const inFlight = new Set();

function reducedMotion() {
    return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export function capture(selector) {
    const el = document.querySelector(selector);

    if (!el) return null;

    const r = el.getBoundingClientRect();

    return [r.left, r.top, r.width, r.height];
}

export function captureMany(selectors) {
    const out = [];

    for (const selector of selectors.split('|')) {
        const el = selector ? document.querySelector(selector) : null;

        if (!el) {
            out.push(0, 0, 0, 0);
            continue;
        }

        const r = el.getBoundingClientRect();
        out.push(r.left, r.top, r.width, r.height);
    }

    return out;
}

export function flyFrom(targetSelector, fromSelector, flip, delay) {
    const el = document.querySelector(fromSelector);

    if (!el) return;

    const r = el.getBoundingClientRect();

    if (el.classList.contains('card')) {
        fly(targetSelector, r.left, r.top, r.width, r.height, flip, delay);
        return;
    }

    const target = document.querySelector(targetSelector);

    if (!target) return;

    const t = target.getBoundingClientRect();
    const width = t.width || r.width;
    const height = t.height || r.height;

    fly(targetSelector, r.left + r.width / 2 - width / 2, r.top + r.height / 2 - height / 2, width, height, flip, delay);
}

export function flyFromRect(targetSelector, left, top, width, height, flip, delay) {
    fly(targetSelector, left, top, width, height, flip, delay);
}

function fly(targetSelector, fromLeft, fromTop, fromWidth, fromHeight, flip, delay) {
    try {
        if (inFlight.has(targetSelector)) return;

        const target = document.querySelector(targetSelector);

        if (!target || !(fromWidth > 0)) return;

        if (reducedMotion()) return;

        const previous = target.style.visibility;
        target.style.visibility = 'hidden';
        inFlight.add(targetSelector);

        let settled = false;

        const done = () => {
            if (settled) return;
            settled = true;

            inFlight.delete(targetSelector);
            target.style.visibility = previous;
        };

        const wait = Math.max(0, delay || 0);

        setTimeout(() => launch(target, targetSelector, fromLeft, fromTop, fromWidth, fromHeight, flip, done), wait);
        setTimeout(done, wait + (flip ? FLIP_DURATION : DURATION) + 900);
    } catch {

    }
}

function launch(target, targetSelector, fromLeft, fromTop, fromWidth, fromHeight, flip, done) {
    try {
        if (!target.isConnected) {
            done();
            return;
        }

        reveal(target);

        const to = target.getBoundingClientRect();

        if (!to.width) {
            done();
            return;
        }

        const dx = fromLeft - to.left;
        const dy = fromTop - to.top;
        const distance = Math.hypot(dx, dy);

        if (distance < 8) {
            done();
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'fly-ghost';
        wrapper.style.left = `${to.left}px`;
        wrapper.style.top = `${to.top}px`;
        wrapper.style.width = `${to.width}px`;
        wrapper.style.height = `${to.height}px`;

        const ghost = target.cloneNode(true);
        ghost.removeAttribute('id');
        ghost.style.position = 'absolute';
        ghost.style.inset = '0';
        ghost.style.width = '100%';
        ghost.style.height = '100%';
        ghost.style.margin = '0';
        ghost.style.transition = 'none';
        ghost.style.animation = 'none';
        ghost.style.visibility = 'visible';

        wrapper.appendChild(ghost);
        document.body.appendChild(wrapper);

        const scale = fromWidth / to.width;

        const lift = Math.min(46, distance * 0.16);
        const midX = dx * 0.5;
        const midY = dy * 0.5 - lift;
        const midScale = (scale + 1) / 2;

        const tilt = Math.max(-9, Math.min(9, dx * 0.03));

        const duration = flip ? FLIP_DURATION : DURATION;

        const travel = wrapper.animate(
            [
                { transform: `translate(${dx}px, ${dy}px) scale(${scale}) rotate(${tilt}deg)` },
                { transform: `translate(${midX}px, ${midY}px) scale(${midScale}) rotate(${tilt * 0.45}deg)`, offset: 0.5 },
                { transform: 'translate(0, 0) scale(1) rotate(0deg)' }
            ],
            { duration, easing: EASE, fill: 'backwards' }
        );

        let flipAnim = null;

        if (flip) {
            flipAnim = ghost.animate(
                [
                    { transform: 'rotateY(180deg)' },
                    { transform: 'rotateY(180deg)', offset: 0.25 },
                    { transform: 'rotateY(0deg)', offset: 0.85 },
                    { transform: 'rotateY(0deg)' }
                ],
                { duration, easing: 'cubic-bezier(0.4, 0, 0.2, 1)', fill: 'backwards' }
            );
        }

        const finish = () => {
            wrapper.remove();
            done();
        };

        Promise.all([travel.finished, flipAnim ? flipAnim.finished : Promise.resolve()])
               .then(finish, finish);
    } catch {
        done();
    }
}

export function flyToSeat(pileSelector, cloneSelector, seatSelector, faceUp, delay) {
    try {
        if (reducedMotion()) return;

        const pile = document.querySelector(pileSelector);
        const seat = document.querySelector(seatSelector);

        if (!pile || !seat) return;

        const from = pile.getBoundingClientRect();

        if (!(from.width > 0)) return;

        const source = cloneSelector ? document.querySelector(cloneSelector) : null;
        let ghost;

        if (source) {
            ghost = source.cloneNode(true);
            ghost.removeAttribute('id');
            ghost.removeAttribute('data-card-id');
        } else {
            ghost = document.createElement('div');
            ghost.className = 'card card--facedown';
            ghost.innerHTML = '<div class="card__back"></div>';
        }

        setTimeout(() => launchToSeat(ghost, from, seat, faceUp), Math.max(0, delay || 0));
    } catch {

    }
}

function launchToSeat(ghost, from, seat, faceUp) {
    try {
        if (!seat.isConnected) return;

        const anchor = seat.querySelector('.seat__avatar') || seat;
        const to = anchor.getBoundingClientRect();

        if (!to.width) return;

        const wrapper = document.createElement('div');
        wrapper.className = `fly-ghost fly-ghost--seat${faceUp ? ' fly-ghost--taken' : ''}`;
        wrapper.style.left = `${from.left}px`;
        wrapper.style.top = `${from.top}px`;
        wrapper.style.width = `${from.width}px`;
        wrapper.style.height = `${from.height}px`;

        ghost.style.position = 'absolute';
        ghost.style.inset = '0';
        ghost.style.width = '100%';
        ghost.style.height = '100%';
        ghost.style.margin = '0';
        ghost.style.transition = 'none';
        ghost.style.animation = 'none';
        ghost.style.visibility = 'visible';

        wrapper.appendChild(ghost);
        document.body.appendChild(wrapper);

        const dx = to.left + to.width / 2 - (from.left + from.width / 2);
        const dy = to.top + to.height / 2 - (from.top + from.height / 2);
        const distance = Math.hypot(dx, dy);
        const lift = Math.min(60, distance * 0.2);
        const end = Math.max(0.18, to.width / from.width * 0.9);
        const tilt = Math.max(-14, Math.min(14, dx * 0.04));
        const duration = faceUp ? 820 : 520;

        const frames = faceUp
            ? [
                { transform: 'translate(0, 0) scale(1) rotate(0deg)', opacity: 1 },
                { transform: `translate(0, -14px) scale(1.18) rotate(0deg)`, opacity: 1, offset: 0.28 },
                { transform: `translate(${dx * 0.5}px, ${dy * 0.5 - lift}px) scale(0.72) rotate(${tilt * 0.5}deg)`, opacity: 1, offset: 0.64 },
                { transform: `translate(${dx}px, ${dy}px) scale(${end}) rotate(${tilt}deg)`, opacity: 0 }
            ]
            : [
                { transform: 'translate(0, 0) scale(1) rotate(0deg)', opacity: 1 },
                { transform: `translate(${dx * 0.5}px, ${dy * 0.5 - lift}px) scale(0.62) rotate(${tilt * 0.5}deg)`, opacity: 1, offset: 0.5 },
                { transform: `translate(${dx}px, ${dy}px) scale(${end}) rotate(${tilt}deg)`, opacity: 0 }
            ];

        const travel = wrapper.animate(frames, { duration, easing: EASE, fill: 'forwards' });

        const finish = () => {
            wrapper.remove();

            if (seat.isConnected) {
                anchor.animate(
                    [{ transform: 'scale(1)' }, { transform: 'scale(1.22)' }, { transform: 'scale(1)' }],
                    { duration: 320, easing: 'cubic-bezier(0.3, 1.6, 0.5, 1)' });
            }
        };

        travel.finished.then(finish, finish);
    } catch {

    }
}

function reveal(target) {
    const scroller = target.closest('.melds');

    if (!scroller) return;

    const box = scroller.getBoundingClientRect();
    const r = target.getBoundingClientRect();

    if (r.left < box.left || r.right > box.right)
        target.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}
