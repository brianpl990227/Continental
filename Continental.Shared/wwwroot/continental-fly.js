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

export function flyFrom(targetSelector, fromSelector, flip) {
    const el = document.querySelector(fromSelector);

    if (!el) return;

    const r = el.getBoundingClientRect();

    fly(targetSelector, r.left, r.top, r.width, r.height, flip);
}

export function flyFromRect(targetSelector, left, top, width, height, flip) {
    fly(targetSelector, left, top, width, height, flip);
}

function fly(targetSelector, fromLeft, fromTop, fromWidth, fromHeight, flip) {
    try {
        if (inFlight.has(targetSelector)) return;

        const target = document.querySelector(targetSelector);

        if (!target || !(fromWidth > 0)) return;

        const from = { left: fromLeft, top: fromTop, width: fromWidth, height: fromHeight };

        const to = target.getBoundingClientRect();

        if (!to.width || !from.width) return;

        if (reducedMotion()) return;

        const dx = from.left - to.left;
        const dy = from.top - to.top;
        const distance = Math.hypot(dx, dy);

        if (distance < 8) return;

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
        ghost.style.visibility = 'visible';

        wrapper.appendChild(ghost);
        document.body.appendChild(wrapper);

        const previous = target.style.visibility;
        target.style.visibility = 'hidden';
        inFlight.add(targetSelector);

        const scale = from.width / to.width;

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

        let settled = false;

        const done = () => {
            if (settled) return;
            settled = true;

            inFlight.delete(targetSelector);
            wrapper.remove();
            target.style.visibility = previous;
        };

        Promise.all([travel.finished, flipAnim ? flipAnim.finished : Promise.resolve()])
               .then(done, done);

        setTimeout(done, duration + 900);
    } catch {

    }
}
