const COLORS = ['#F2B33D', '#C9A227', '#5FA98A', '#E4572E', '#7FB5D5', '#FBF7EF'];

function reducedMotion() {
    return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export function burst(count) {
    try {
        if (reducedMotion()) return;

        const existing = document.getElementById('confetti-layer');
        if (existing) existing.remove();

        const canvas = document.createElement('canvas');
        canvas.id = 'confetti-layer';
        canvas.style.cssText =
            'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:400';

        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = window.innerWidth * dpr;
        canvas.height = window.innerHeight * dpr;

        document.body.appendChild(canvas);

        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const w = window.innerWidth;
        const h = window.innerHeight;
        const n = Math.max(24, Math.min(count || 90, 160));
        const pieces = [];

        for (let i = 0; i < n; i++) {
            pieces.push({
                x: w * (0.15 + Math.random() * 0.7),
                y: -20 - Math.random() * h * 0.4,
                vx: (Math.random() - 0.5) * 2.4,
                vy: 2 + Math.random() * 3.4,
                size: 5 + Math.random() * 7,
                ratio: 0.4 + Math.random() * 0.6,
                spin: (Math.random() - 0.5) * 0.24,
                angle: Math.random() * Math.PI * 2,
                sway: 0.6 + Math.random() * 1.6,
                phase: Math.random() * Math.PI * 2,
                color: COLORS[(Math.random() * COLORS.length) | 0]
            });
        }

        const started = performance.now();
        const life = 4200;

        function frame(now) {
            const elapsed = now - started;

            if (elapsed > life) {
                canvas.remove();
                return;
            }

            ctx.clearRect(0, 0, w, h);

            const fade = elapsed > life - 900 ? (life - elapsed) / 900 : 1;
            ctx.globalAlpha = Math.max(0, fade);

            let alive = 0;

            for (const p of pieces) {
                p.x += p.vx + Math.sin(now / 320 + p.phase) * p.sway;
                p.y += p.vy;
                p.vy += 0.035;
                p.angle += p.spin;

                if (p.y < h + 40) alive++;

                ctx.save();
                ctx.translate(p.x, p.y);
                ctx.rotate(p.angle);
                ctx.fillStyle = p.color;
                ctx.fillRect(-p.size / 2, -(p.size * p.ratio) / 2, p.size, p.size * p.ratio);
                ctx.restore();
            }

            if (alive === 0) {
                canvas.remove();
                return;
            }

            requestAnimationFrame(frame);
        }

        requestAnimationFrame(frame);
    } catch {
        /* decoration only */
    }
}

export function clear() {
    const existing = document.getElementById('confetti-layer');
    if (existing) existing.remove();
}
