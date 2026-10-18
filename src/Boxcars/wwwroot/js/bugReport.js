(() => {
    // Menus, dialogs and overlays aren't part of what the player was looking at.
    function isPartOfThePage(node) {
        if (!node.classList) {
            return true;
        }

        // The map's rail click targets are invisible, and skipping them saves copying thousands of elements.
        return !node.classList.contains("mud-popover-provider")
            && !node.classList.contains("mud-dialog-container")
            && !node.classList.contains("mud-overlay")
            && !node.classList.contains("game-map-railroad-hitbox")
            && node.id !== "blazor-error-ui";
    }

    // Copying the page blocks the browser, so let it draw the "taking a screenshot" overlay first.
    function waitForNextPaint() {
        return new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    }

    // Screen sharing captures the real pixels at once, after the browser asks the player's permission.
    function canShareScreen() {
        return !!navigator.mediaDevices?.getDisplayMedia;
    }

    // Takes a JPEG for a bug report: from screen sharing where the browser has it, otherwise by drawing the page.
    // Returns an empty array when there's no screenshot (the player declined, or the page can't be drawn), so the
    // report still goes out without one.
    async function captureScreen() {
        return canShareScreen() ? await captureSharedScreen() : await drawPage();
    }

    async function captureSharedScreen() {
        let stream;
        try {
            stream = await navigator.mediaDevices.getDisplayMedia({
                video: { displaySurface: "browser" },
                audio: false,
                // Offer this tab first, and leave out other windows and whole screens where the browser allows.
                preferCurrentTab: true,
                selfBrowserSurface: "include",
                surfaceSwitching: "exclude",
                monitorTypeSurfaces: "exclude"
            });
        } catch {
            // The player declined to share.
            return new Uint8Array(0);
        }

        try {
            const video = document.createElement("video");
            video.muted = true;
            video.srcObject = stream;
            await video.play();
            await waitForNextPaint();

            const canvas = document.createElement("canvas");
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            canvas.getContext("2d").drawImage(video, 0, 0);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.8));
            return blob ? new Uint8Array(await blob.arrayBuffer()) : new Uint8Array(0);
        } catch {
            return new Uint8Array(0);
        } finally {
            stream.getTracks().forEach(track => track.stop());
        }
    }

    // For browsers without screen sharing (iPad Safari): rebuilds the page as an image, which takes several seconds.
    async function drawPage() {
        if (!window.htmlToImage) {
            return new Uint8Array(0);
        }

        try {
            await waitForNextPaint();
            const started = performance.now();
            const blob = await window.htmlToImage.toBlob(document.body, {
                pixelRatio: 1,
                type: "image/jpeg",
                quality: 0.8,
                backgroundColor: getComputedStyle(document.body).backgroundColor,
                filter: isPartOfThePage,
                // Embedding the web fonts means downloading them every time; the system font is fine for a bug report.
                skipFonts: true
            });
            console.debug(`Bug report screenshot took ${Math.round(performance.now() - started)} ms.`);
            return blob ? new Uint8Array(await blob.arrayBuffer()) : new Uint8Array(0);
        } catch {
            return new Uint8Array(0);
        }
    }

    window.bugReport = {
        canShareScreen,
        captureScreen
    };
})();
