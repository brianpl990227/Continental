let ctx = null;
let master = null;
let muted = false;
let pack = null;

const PACKS = {
    clasico: {},
    suave: { type: "sine", gain: 0.8, noise: 0.6, stretch: 1.25 },
    retro: { type: "square", gain: 0.5, noise: 0.45, semitones: true, stretch: 0.8 },
    casino: { type: "triangle", gain: 0.9, sparkle: true },
    cristal: { type: "sine", gain: 0.75, octave: true, stretch: 1.6, noise: 0.7 }
};

function currentPack() {
    if (pack) return PACKS[pack] || PACKS.clasico;

    try {
        return PACKS[document.documentElement.dataset.sound] || PACKS.clasico;
    } catch {
        return PACKS.clasico;
    }
}

const MUTE_KEY = "continental.muted";

function context() {
    if (!ctx) {
        const Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;

        ctx = new Ctor();
        master = ctx.createGain();
        master.gain.value = 0.5;
        master.connect(ctx.destination);
    }

    if (ctx.state === "suspended") ctx.resume().catch(() => { });

    return ctx;
}

function rand(min, max) {
    return min + Math.random() * (max - min);
}

function noise(opts) {
    const c = context();
    if (!c || muted) return;

    const style = currentPack();
    const { duration = 0.08, freq = 1800, q = 1.2, type = "bandpass", sweep = 0 } = opts;
    const gain = (opts.gain ?? 0.3) * (style.noise ?? 1);

    const frames = Math.max(1, Math.floor(c.sampleRate * duration));
    const buffer = c.createBuffer(1, frames, c.sampleRate);
    const data = buffer.getChannelData(0);

    for (let i = 0; i < frames; i++) data[i] = Math.random() * 2 - 1;

    const src = c.createBufferSource();
    src.buffer = buffer;

    const filter = c.createBiquadFilter();
    filter.type = type;
    filter.frequency.setValueAtTime(freq, c.currentTime);
    filter.Q.value = q;

    if (sweep) {
        filter.frequency.exponentialRampToValueAtTime(
            Math.max(80, freq * sweep), c.currentTime + duration);
    }

    const env = c.createGain();
    env.gain.setValueAtTime(0, c.currentTime);
    env.gain.linearRampToValueAtTime(gain, c.currentTime + 0.004);
    env.gain.exponentialRampToValueAtTime(0.0001, c.currentTime + duration);

    src.connect(filter).connect(env).connect(master);
    src.start();
    src.stop(c.currentTime + duration + 0.02);
}

function tone(opts) {
    const c = context();
    if (!c || muted) return;

    const style = currentPack();
    let { freq = 660, duration = 0.16, type = "triangle", gain = 0.16, delay = 0, glide = 0 } = opts;

    if (style.type && !opts.keep) type = style.type;
    if (style.gain) gain *= style.gain;
    if (style.stretch) duration *= style.stretch;
    if (style.octave) freq *= 2;
    if (style.semitones) freq = 440 * Math.pow(2, Math.round(12 * Math.log2(freq / 440)) / 12);

    if (style.sparkle && !opts.child) {
        tone({ ...opts, freq: freq * 2, gain: (opts.gain ?? 0.16) * 0.3, delay: delay + 0.025, child: true });
    }

    const osc = c.createOscillator();
    osc.type = type;

    const at = c.currentTime + delay;
    osc.frequency.setValueAtTime(freq, at);

    if (glide) osc.frequency.exponentialRampToValueAtTime(glide, at + duration);

    const env = c.createGain();
    env.gain.setValueAtTime(0, at);
    env.gain.linearRampToValueAtTime(gain, at + 0.012);
    env.gain.exponentialRampToValueAtTime(0.0001, at + duration);

    osc.connect(env).connect(master);
    osc.start(at);
    osc.stop(at + duration + 0.02);
}

function chord(freqs, opts = {}) {
    freqs.forEach((f, i) => tone({
        freq: f,
        delay: (opts.stagger ?? 0.07) * i,
        duration: opts.duration ?? 0.28,
        gain: opts.gain ?? 0.13,
        type: opts.type ?? "triangle"
    }));
}

const sounds = {

    deal() {
        noise({ duration: rand(0.05, 0.075), freq: rand(2600, 3400), q: 0.8, gain: 0.22, sweep: 0.35 });
    },

    draw() {
        noise({ duration: 0.05, freq: rand(3000, 3800), q: 1.0, gain: 0.2, sweep: 0.5 });
    },

    place() {
        noise({ duration: 0.09, freq: rand(900, 1300), q: 0.7, gain: 0.26, type: "lowpass", sweep: 0.4 });
        tone({ freq: rand(105, 135), duration: 0.06, type: "sine", gain: 0.1 });
    },

    select() {
        noise({ duration: 0.03, freq: 4200, q: 1.6, gain: 0.1, sweep: 0.7 });
    },

    shuffle() {
        const c = context();
        if (!c || muted) return;

        for (let i = 0; i < 11; i++) {
            setTimeout(() => noise({
                duration: 0.045,
                freq: rand(1900, 3200),
                q: 0.7,
                gain: 0.13,
                sweep: 0.4
            }), i * rand(28, 52));
        }
    },

    turn() {
        chord([659.25, 987.77], { stagger: 0.09, duration: 0.3, gain: 0.12 });
    },

    laydown() {
        sounds.place();
        chord([523.25, 659.25, 783.99], { stagger: 0.06, duration: 0.4, gain: 0.13 });
    },

    opponentLaydown() {
        chord([392.0, 493.88], { stagger: 0.06, duration: 0.25, gain: 0.07 });
    },

    steal() {
        tone({ freq: 880, duration: 0.1, type: "square", gain: 0.1 });
        tone({ freq: 1174.66, duration: 0.13, type: "square", gain: 0.1, delay: 0.12 });
    },

    tick() {
        noise({ duration: 0.022, freq: 2400, q: 3.0, gain: 0.12 });
    },

    roundEnd() {
        chord([523.25, 698.46], { stagger: 0.08, duration: 0.35, gain: 0.11 });
    },

    win() {
        chord([523.25, 659.25, 783.99, 1046.5], { stagger: 0.1, duration: 0.5, gain: 0.14 });
    },

    lose() {
        chord([493.88, 415.3, 349.23], { stagger: 0.13, duration: 0.45, gain: 0.1, type: "sine" });
    },

    celebrate() {
        chord([523.25, 659.25, 783.99, 1046.5, 1318.5], { stagger: 0.07, duration: 0.55, gain: 0.13 });

        const c = context();
        if (!c || muted) return;

        for (let i = 0; i < 14; i++) {
            setTimeout(() => noise({
                duration: 0.05,
                freq: rand(4000, 9000),
                q: 2.4,
                gain: 0.07,
                sweep: 1.6
            }), 90 + i * rand(35, 90));
        }
    },

    flop() {
        const c = context();
        if (!c || muted) return;

        const osc = c.createOscillator();
        osc.type = "sawtooth";
        osc.frequency.setValueAtTime(330, c.currentTime);
        osc.frequency.exponentialRampToValueAtTime(82, c.currentTime + 0.75);

        const wobble = c.createOscillator();
        wobble.frequency.value = 5.5;
        const wobbleDepth = c.createGain();
        wobbleDepth.gain.value = 14;
        wobble.connect(wobbleDepth).connect(osc.frequency);

        const filter = c.createBiquadFilter();
        filter.type = "lowpass";
        filter.frequency.setValueAtTime(1400, c.currentTime);
        filter.frequency.exponentialRampToValueAtTime(420, c.currentTime + 0.75);

        const env = c.createGain();
        env.gain.setValueAtTime(0, c.currentTime);
        env.gain.linearRampToValueAtTime(0.11, c.currentTime + 0.04);
        env.gain.setValueAtTime(0.11, c.currentTime + 0.5);
        env.gain.exponentialRampToValueAtTime(0.0001, c.currentTime + 0.8);

        osc.connect(filter).connect(env).connect(master);
        osc.start();
        wobble.start();
        osc.stop(c.currentTime + 0.85);
        wobble.stop(c.currentTime + 0.85);
    },

    error() {
        tone({ freq: 180, duration: 0.14, type: "sawtooth", gain: 0.1, glide: 120 });
    },

    tap() {
        noise({ duration: 0.02, freq: 2800, q: 2.2, gain: 0.08 });
    },

    chat() {
        tone({ freq: 1046.5, duration: 0.07, type: "sine", gain: 0.05 });
        tone({ freq: 1318.5, duration: 0.09, type: "sine", gain: 0.05, delay: 0.07 });
    },

    mission() {
        chord([659.25, 783.99, 987.77, 1318.5], { stagger: 0.075, duration: 0.42, gain: 0.12 });
        tone({ freq: 1567.98, duration: 0.6, type: "sine", gain: 0.07, delay: 0.34 });

        const c = context();
        if (!c || muted) return;

        for (let i = 0; i < 8; i++) {
            setTimeout(() => noise({ duration: 0.04, freq: rand(5000, 9000), q: 3, gain: 0.05, sweep: 1.4 }), 300 + i * rand(40, 80));
        }
    },

    levelup() {
        chord([392.0, 523.25, 659.25, 783.99], { stagger: 0.09, duration: 0.35, gain: 0.12 });
        chord([523.25, 659.25, 783.99, 1046.5, 1318.5], { stagger: 0.06, duration: 0.8, gain: 0.12 });
        tone({ freq: 2093.0, duration: 0.9, type: "sine", gain: 0.05, delay: 0.7 });

        const c = context();
        if (!c || muted) return;

        for (let i = 0; i < 18; i++) {
            setTimeout(() => noise({ duration: 0.05, freq: rand(4000, 10000), q: 2.4, gain: 0.06, sweep: 1.6 }), 450 + i * rand(35, 80));
        }
    },

    victory() {
        const c = context();
        if (!c || muted) return;

        [0, 0.12, 0.24].forEach(d => tone({ freq: 82, duration: 0.22, type: "sine", gain: 0.24, delay: d, keep: true }));
        [[523.25, 0.3], [523.25, 0.45], [523.25, 0.6], [659.25, 0.78], [783.99, 1.02]].forEach(([f, d]) =>
            tone({ freq: f, duration: 0.2, type: "sawtooth", gain: 0.07, delay: d }));
        setTimeout(() => chord([523.25, 659.25, 783.99, 1046.5, 1318.5, 1567.98], { stagger: 0.04, duration: 1.4, gain: 0.11 }), 1250);
        tone({ freq: 2093.0, duration: 1.2, type: "sine", gain: 0.05, delay: 1.6 });

        for (let i = 0; i < 26; i++) {
            setTimeout(() => noise({ duration: 0.05, freq: rand(4000, 10000), q: 2.4, gain: 0.06, sweep: 1.6 }), 1250 + i * rand(30, 90));
        }
    },

    defeat() {
        const c = context();
        if (!c || muted) return;

        const notes = [[392.0, 0, 0.42], [369.99, 0.5, 0.42], [349.23, 1.0, 0.42], [329.63, 1.5, 1.4]];

        notes.forEach(([freq, delay, duration], i) => {
            const at = c.currentTime + delay;
            const osc = c.createOscillator();
            osc.type = "sawtooth";
            osc.frequency.setValueAtTime(freq * 1.03, at);
            osc.frequency.exponentialRampToValueAtTime(freq, at + 0.12);

            if (i === 3) {
                const wobble = c.createOscillator();
                wobble.frequency.value = 5;
                const depth = c.createGain();
                depth.gain.value = 9;
                wobble.connect(depth).connect(osc.frequency);
                wobble.start(at + 0.25);
                wobble.stop(at + duration);
                osc.frequency.exponentialRampToValueAtTime(freq * 0.82, at + duration);
            }

            const filter = c.createBiquadFilter();
            filter.type = "lowpass";
            filter.frequency.value = 900;

            const env = c.createGain();
            env.gain.setValueAtTime(0, at);
            env.gain.linearRampToValueAtTime(0.13, at + 0.05);
            env.gain.setValueAtTime(0.13, at + duration - 0.15);
            env.gain.exponentialRampToValueAtTime(0.0001, at + duration);

            osc.connect(filter).connect(env).connect(master);
            osc.start(at);
            osc.stop(at + duration + 0.05);
        });
    },

    start() {
        const c = context();
        if (!c || muted) return;

        [0, 0.14, 0.28].forEach((d, i) => tone({ freq: 98 + i * 8, duration: 0.18, type: "sine", gain: 0.2, delay: d, keep: true }));
        chord([392.0, 523.25, 659.25, 783.99, 1046.5], { stagger: 0.05, duration: 0.7, gain: 0.12 });
        tone({ freq: 1567.98, duration: 0.8, type: "sine", gain: 0.05, delay: 0.3 });

        for (let i = 0; i < 12; i++) {
            setTimeout(() => noise({ duration: 0.05, freq: rand(4000, 9000), q: 2.4, gain: 0.05, sweep: 1.6 }), 250 + i * rand(30, 70));
        }
    },

    unlock() {
        tone({ freq: 1046.5, duration: 0.14, type: "sine", gain: 0.08 });
        tone({ freq: 1567.98, duration: 0.3, type: "sine", gain: 0.08, delay: 0.1 });
    },

    swap() {
        noise({ duration: 0.06, freq: rand(2200, 2800), q: 1.2, gain: 0.16, sweep: 0.6 });
        chord([783.99, 1046.5], { stagger: 0.07, duration: 0.22, gain: 0.09 });
    }
};

export function play(name) {
    try {
        const fn = sounds[name];
        if (fn) fn();
    } catch {

    }
}

export function playWith(name, packName) {
    const previous = pack;
    const wasMuted = muted;
    pack = packName;
    muted = false;

    try {
        const fn = sounds[name];
        if (fn) fn();
    } catch {
    } finally {
        pack = previous;
        muted = wasMuted;
    }
}

export function setMuted(value) {
    muted = !!value;

    try {
        localStorage.setItem(MUTE_KEY, muted ? "1" : "0");
    } catch {

    }

    return muted;
}

export function isMuted() {
    try {
        muted = localStorage.getItem(MUTE_KEY) === "1";
    } catch {
        muted = false;
    }

    return muted;
}

export function unlock() {
    context();
}
