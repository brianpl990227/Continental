export function apply(back, felt, trail, sound, victory) {
    try {
        const d = document.documentElement.dataset;
        d.back = back || 'clasico';
        d.felt = felt || 'verde';
        d.trail = trail || 'ninguna';
        d.sound = sound || 'clasico';
        d.victory = victory || 'confeti';
    } catch {
    }
}

export async function preview(kind, value) {
    try {
        if (kind === 'Sound') {
            const audio = await import('./continental-audio.js');
            audio.unlock();
            audio.playWith('turn', value);
            setTimeout(() => audio.playWith('laydown', value), 420);
            setTimeout(() => audio.playWith('mission', value), 1150);
        } else if (kind === 'Victory') {
            const confetti = await import('./continental-confetti.js');
            confetti.burst(120, value);
        }
    } catch {
    }
}
