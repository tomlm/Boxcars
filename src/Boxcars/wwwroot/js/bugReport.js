(() => {
    // Menus, dialogs and overlays aren't part of what the player was looking at.
    function isPartOfThePage(node) {
        if (!node.classList) {
            return true;
        }

        return !node.classList.contains("mud-popover-provider")
            && !node.classList.contains("mud-dialog-container")
            && !node.classList.contains("mud-overlay")
            && node.id !== "blazor-error-ui";
    }

    // Draws the page to a JPEG for a bug report. Returns an empty array when the page can't be drawn, so the report
    // still goes out without a screenshot.
    async function captureScreen() {
        if (!window.htmlToImage) {
            return new Uint8Array(0);
        }

        try {
            const blob = await window.htmlToImage.toBlob(document.body, {
                pixelRatio: 1,
                type: "image/jpeg",
                quality: 0.8,
                backgroundColor: getComputedStyle(document.body).backgroundColor,
                filter: isPartOfThePage
            });
            return blob ? new Uint8Array(await blob.arrayBuffer()) : new Uint8Array(0);
        } catch {
            return new Uint8Array(0);
        }
    }

    window.bugReport = {
        captureScreen
    };
})();
