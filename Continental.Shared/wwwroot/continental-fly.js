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

function reveal(target) {
    const scroller = target.closest('.melds');

    if (!scroller) return;

    const box = scroller.getBoundingClientRect();
    const r = target.getBoundingClientRect();

    if (r.left < box.left || r.right > box.right)
        target.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}
