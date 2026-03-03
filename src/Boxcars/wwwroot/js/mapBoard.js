window.mapBoard = {
    getRelativePoint(element, clientX, clientY) {
        if (!element) {
            return { x: 0, y: 0, width: 1, height: 1 };
        }

        const rect = element.getBoundingClientRect();
        return {
            x: clientX - rect.left,
            y: clientY - rect.top,
            width: rect.width,
            height: rect.height
        };
    }
};
