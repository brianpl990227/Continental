const PALETTES = {
    confeti: ['#F2B33D', '#C9A227', '#5FA98A', '#E4572E', '#7FB5D5', '#FBF7EF'],
    palos: ['#FBF7EF', '#C2352B', '#FBF7EF', '#E4572E'],
    estrellas: ['#F2B33D', '#FFE39A', '#FFF6E2', '#C9A227'],
    monedas: ['#F2B33D', '#C9A227', '#E8C35A'],
    comodines: ['#FBF7EF'],
    fuegos: ['#F2B33D', '#E4572E', '#7FB5D5', '#BC8CE8', '#5FA98A', '#EF7FA9'],
    oro: ['#F2B33D', '#C9A227', '#FFF6E2', '#E8C35A', '#FFE39A'],
    arcoiris: ['#E4572E', '#F2B33D', '#FFE39A', '#5FA98A', '#7FB5D5', '#BC8CE8', '#EF7FA9']
};

const SUITS = ['♠', '♥', '♦', '♣'];

function reducedMotion() {
    return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function currentStyle() {
    try {
        return document.documentElement.dataset.victory || 'confeti';
    } catch {
        return 'confeti';
    }
}

function layer(id, z) {
    const existing = document.getElementById(id);
    if (existing) existing.remove();

    const canvas = document.createElement('canvas');
    canvas.id = id;
    canvas.style.cssText = `position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:${z}`;

    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = window.innerWidth * dpr;
    canvas.height = window.innerHeight * dpr;

    document.body.appendChild(canvas);

    const ctx = canvas.getContext('2d');
    ctx.scale(dpr, dpr);

    return { canvas, ctx, w: window.innerWidth, h: window.innerHeight };
}

function pick(list) {
    return list[(Math.random() * list.length) | 0];
}

function star(ctx, r) {
    ctx.beginPath();
    for (let i = 0; i < 10; i++) {
        const radius = i % 2 === 0 ? r : r * 0.45;
        const a = (Math.PI / 5) * i - Math.PI / 2;
        ctx.lineTo(Math.cos(a) * radius, Math.sin(a) * radius);
    }
    ctx.closePath();
    ctx.fill();
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

function drawPiece(ctx, p, style, now) {
    ctx.fillStyle = p.color;

    switch (style) {
        case 'palos': {
            ctx.font = `700 ${p.size * 2.1}px system-ui, sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = p.glyph === '♥' || p.glyph === '♦' ? '#E4572E' : '#FBF7EF';
            ctx.fillText(p.glyph, 0, 0);
            break;
        }
        case 'estrellas':
            star(ctx, p.size * 0.9);
            break;
        case 'monedas': {
            const squash = Math.abs(Math.cos(p.angle * 2));
            ctx.scale(Math.max(0.15, squash), 1);
            ctx.beginPath();
            ctx.arc(0, 0, p.size * 0.8, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = 'rgba(255,255,255,0.45)';
            ctx.beginPath();
            ctx.arc(-p.size * 0.22, -p.size * 0.22, p.size * 0.28, 0, Math.PI * 2);
            ctx.fill();
            break;
        }
        case 'comodines': {
            const w = p.size * 1.5;
            const h = p.size * 2.1;
            roundRect(ctx, -w / 2, -h / 2, w, h, 2.5);
            ctx.fillStyle = '#FBF7EF';
            ctx.fill();
            ctx.strokeStyle = 'rgba(20,23,26,0.25)';
            ctx.lineWidth = 0.8;
            ctx.stroke();
            ctx.fillStyle = '#C9A227';
            star(ctx, p.size * 0.5);
            break;
        }
        case 'oro': {
            const twinkle = 0.55 + 0.45 * Math.sin(now / 90 + p.phase * 4);
            ctx.globalAlpha *= twinkle;
            ctx.fillRect(-p.size / 2, -(p.size * p.ratio) / 2, p.size, p.size * p.ratio);
            break;
        }
        case 'arcoiris':
            ctx.fillRect(-p.size * 0.18, -p.size * 1.3, p.size * 0.36, p.size * 2.6);
            break;
        default:
            ctx.fillRect(-p.size / 2, -(p.size * p.ratio) / 2, p.size, p.size * p.ratio);
    }
}

function rain(count, style) {
    const { canvas, ctx, w, h } = layer('confetti-layer', 400);
    const palette = PALETTES[style] || PALETTES.confeti;
    const n = Math.max(24, Math.min(count || 90, 170));
    const pieces = [];

    for (let i = 0; i < n; i++) {
        pieces.push({
            x: w * (0.1 + Math.random() * 0.8),
            y: -20 - Math.random() * h * 0.45,
            vx: (Math.random() - 0.5) * 2.4,
            vy: 2 + Math.random() * 3.4,
            size: 5 + Math.random() * 7,
            ratio: 0.4 + Math.random() * 0.6,
            spin: (Math.random() - 0.5) * (style === 'palos' ? 0.1 : 0.24),
            angle: Math.random() * Math.PI * 2,
            sway: 0.6 + Math.random() * 1.6,
            phase: Math.random() * Math.PI * 2,
            color: pick(palette),
            glyph: pick(SUITS)
        });
    }

    const started = performance.now();
    const life = 4400;

    function frame(now) {
        const elapsed = now - started;

        if (elapsed > life) {
            canvas.remove();
            return;
        }

        ctx.clearRect(0, 0, w, h);
        const fade = elapsed > life - 900 ? (life - elapsed) / 900 : 1;
        let alive = 0;

        for (const p of pieces) {
            p.x += p.vx + Math.sin(now / 320 + p.phase) * p.sway;
            p.y += p.vy;
            p.vy += 0.035;
            p.angle += p.spin;

            if (p.y < h + 40) alive++;

            ctx.save();
            ctx.globalAlpha = Math.max(0, fade);
            ctx.translate(p.x, p.y);
            ctx.rotate(p.angle);
            drawPiece(ctx, p, style, now);
            ctx.restore();
        }

        if (alive === 0) {
            canvas.remove();
            return;
        }

        requestAnimationFrame(frame);
    }

    requestAnimationFrame(frame);
}

function fireworks(count) {
    const { canvas, ctx, w, h } = layer('confetti-layer', 400);
    const palette = PALETTES.fuegos;
    const shells = Math.max(3, Math.min(8, Math.round((count || 100) / 22)));
    const sparks = [];
    const started = performance.now();
    const life = 4600;
    let launched = 0;

    function explode() {
        const cx = w * (0.18 + Math.random() * 0.64);
        const cy = h * (0.14 + Math.random() * 0.32);
        const color = pick(palette);
        const n = 46;

        for (let i = 0; i < n; i++) {
            const a = (Math.PI * 2 * i) / n + Math.random() * 0.1;
            const speed = 2.2 + Math.random() * 2.6;
            sparks.push({ x: cx, y: cy, px: cx, py: cy, vx: Math.cos(a) * speed, vy: Math.sin(a) * speed, life: 1, color });
        }
    }

    function frame(now) {
        const elapsed = now - started;

        if (launched < shells && elapsed > launched * 420) {
            explode();
            launched++;
        }

        if (elapsed > life) {
            canvas.remove();
            return;
        }

        ctx.globalCompositeOperation = 'destination-out';
        ctx.fillStyle = 'rgba(0,0,0,0.28)';
        ctx.fillRect(0, 0, w, h);
        ctx.globalCompositeOperation = 'lighter';

        for (const s of sparks) {
            if (s.life <= 0) continue;

            s.px = s.x;
            s.py = s.y;
            s.x += s.vx;
            s.y += s.vy;
            s.vx *= 0.975;
            s.vy = s.vy * 0.975 + 0.05;
            s.life -= 0.012;

            ctx.strokeStyle = s.color;
            ctx.globalAlpha = Math.max(0, s.life);
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(s.px, s.py);
            ctx.lineTo(s.x, s.y);
            ctx.stroke();
        }

        ctx.globalAlpha = 1;
        ctx.globalCompositeOperation = 'source-over';
        requestAnimationFrame(frame);
    }

    requestAnimationFrame(frame);
}

export function burst(count, style) {
    try {
        if (reducedMotion()) return;

        const chosen = style || currentStyle();

        if (chosen === 'fuegos') fireworks(count);
        else rain(count, chosen);
    } catch {
    }
}

export function sparkle(x, y, count, tone) {
    try {
        if (reducedMotion()) return;

        const { canvas, ctx, w, h } = layer('sparkle-layer', 520);
        const palette = tone === 'secret' ? ['#BC8CE8', '#EF7FA9', '#FFF6E2'] : PALETTES.estrellas;
        const n = Math.max(10, Math.min(count || 26, 60));
        const cx = x > 0 ? x : w / 2;
        const cy = y > 0 ? y : 90;
        const parts = [];

        for (let i = 0; i < n; i++) {
            const a = Math.random() * Math.PI * 2;
            const speed = 2 + Math.random() * 5;
            parts.push({
                x: cx, y: cy,
                vx: Math.cos(a) * speed, vy: Math.sin(a) * speed - 1.5,
                size: 3 + Math.random() * 5,
                angle: Math.random() * Math.PI,
                spin: (Math.random() - 0.5) * 0.3,
                color: pick(palette)
            });
        }

        const started = performance.now();
        const life = 1500;

        function frame(now) {
            const t = (now - started) / life;

            if (t >= 1) {
                canvas.remove();
                return;
            }

            ctx.clearRect(0, 0, w, h);

            for (const p of parts) {
                p.x += p.vx;
                p.y += p.vy;
                p.vx *= 0.96;
                p.vy = p.vy * 0.96 + 0.12;
                p.angle += p.spin;

                ctx.save();
                ctx.globalAlpha = 1 - t;
                ctx.translate(p.x, p.y);
                ctx.rotate(p.angle);
                ctx.fillStyle = p.color;
                star(ctx, p.size);
                ctx.restore();
            }

            requestAnimationFrame(frame);
        }

        requestAnimationFrame(frame);
    } catch {
    }
}

export function clear() {
    for (const id of ['confetti-layer', 'sparkle-layer']) {
        const existing = document.getElementById(id);
        if (existing) existing.remove();
    }
}
