window.scrollText = {
    init: (container) => {
        const inner = container.querySelector('.scroll-text-inner');
        if (!inner) return;
        const overflow = inner.scrollWidth - container.clientWidth;
        if (overflow > 0) {
            container.style.setProperty('--scroll-distance', `-${overflow}px`);
            inner.classList.add('overflows');
        } else {
            container.style.removeProperty('--scroll-distance');
            inner.classList.remove('overflows');
        }
    }
};
