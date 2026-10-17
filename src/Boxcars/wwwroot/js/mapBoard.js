(() => {
    const states = new WeakMap();
    const heartbeats = new Map();

    function getRelativePoint(element, clientX, clientY) {
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

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function getZoomScale(zoomPercent) {
        return zoomPercent / 100;
    }

    function getMapCenter(state) {
        return {
            x: state.mapLeft + state.mapWidth / 2,
            y: state.mapTop + state.mapHeight / 2
        };
    }

    function applyCameraTransform(state) {
        if (!state.cameraGroup) {
            return;
        }

        const zoomScale = getZoomScale(state.zoomPercent);
        const mapCenter = getMapCenter(state);
        const transform = `translate(${mapCenter.x.toFixed(4)} ${mapCenter.y.toFixed(4)}) scale(${zoomScale.toFixed(6)}) translate(${(-state.centerX).toFixed(4)} ${(-state.centerY).toFixed(4)})`;
        state.cameraGroup.setAttribute("transform", transform);
    }

    function syncViewportToDotNet(state) {
        if (!state.dotNetReference) {
            return;
        }

        const rect = state.element.getBoundingClientRect();
        state.viewportSequence += 1;
        state.dotNetReference.invokeMethodAsync(
            "OnClientViewportChanged",
            state.viewportSequence,
            state.zoomPercent,
            state.centerX,
            state.centerY,
            rect.width,
            rect.height);
    }

    function scheduleViewportCommit(state) {
        if (state.syncHandle) {
            clearTimeout(state.syncHandle);
        }

        state.syncHandle = setTimeout(() => {
            state.syncHandle = null;
            syncViewportToDotNet(state);
        }, 180);
    }

    function updateCursorAnchoredZoom(state, requestedZoom, relativeX, relativeY, width, height) {
        const nextZoom = clamp(requestedZoom, state.minZoom, state.maxZoom);

        if (nextZoom <= state.minZoom) {
            state.zoomPercent = nextZoom;
            const center = getMapCenter(state);
            state.centerX = center.x;
            state.centerY = center.y;
            return;
        }

        const clampedX = clamp(relativeX, 0, width);
        const clampedY = clamp(relativeY, 0, height);

        const currentZoomScale = getZoomScale(state.zoomPercent);
        const nextZoomScale = getZoomScale(nextZoom);

        const currentViewWidth = state.mapWidth / currentZoomScale;
        const currentViewHeight = state.mapHeight / currentZoomScale;
        const nextViewWidth = state.mapWidth / nextZoomScale;
        const nextViewHeight = state.mapHeight / nextZoomScale;

        const currentViewX = state.centerX - currentViewWidth / 2;
        const currentViewY = state.centerY - currentViewHeight / 2;

        const boardPointX = currentViewX + (clampedX / width) * currentViewWidth;
        const boardPointY = currentViewY + (clampedY / height) * currentViewHeight;

        const nextViewX = boardPointX - (clampedX / width) * nextViewWidth;
        const nextViewY = boardPointY - (clampedY / height) * nextViewHeight;

        state.zoomPercent = nextZoom;
        state.centerX = nextViewX + nextViewWidth / 2;
        state.centerY = nextViewY + nextViewHeight / 2;
    }

    function suppressClickAfterPan(state) {
        state.suppressClickUntil = performance.now() + 200;
    }

    function installHandlers(state) {
        const element = state.element;

        state.onWheel = event => {
            event.preventDefault();
            event.stopPropagation();

            const delta = event.deltaY < 0 ? 50 : -50;
            const relativePoint = getRelativePoint(element, event.clientX, event.clientY);
            updateCursorAnchoredZoom(
                state,
                state.zoomPercent + delta,
                relativePoint.x,
                relativePoint.y,
                Math.max(1, relativePoint.width),
                Math.max(1, relativePoint.height));

            applyCameraTransform(state);
            scheduleViewportCommit(state);
        };

        state.onMouseDown = event => {
            if (event.button !== 0) {
                return;
            }

            if (event.target && event.target.closest("[data-mapboard-pan-ignore='true']")) {
                return;
            }

            if (state.zoomPercent <= state.minZoom) {
                return;
            }

            if (state.syncHandle) {
                clearTimeout(state.syncHandle);
                state.syncHandle = null;
            }

            state.isPanning = true;
            state.panMoved = false;
            state.panStartClientX = event.clientX;
            state.panStartClientY = event.clientY;
            state.panStartCenterX = state.centerX;
            state.panStartCenterY = state.centerY;

            element.classList.add("panning");
            event.preventDefault();
        };

        state.onMouseMove = event => {
            if (!state.isPanning) {
                return;
            }

            const relativePoint = getRelativePoint(element, event.clientX, event.clientY);
            const width = Math.max(1, relativePoint.width);
            const height = Math.max(1, relativePoint.height);

            const deltaX = event.clientX - state.panStartClientX;
            const deltaY = event.clientY - state.panStartClientY;

            if (!state.panMoved && (Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2)) {
                state.panMoved = true;
            }

            const zoomScale = getZoomScale(state.zoomPercent);
            const currentViewWidth = state.mapWidth / zoomScale;
            const currentViewHeight = state.mapHeight / zoomScale;

            const boardDeltaX = deltaX * (currentViewWidth / width);
            const boardDeltaY = deltaY * (currentViewHeight / height);

            state.centerX = state.panStartCenterX - boardDeltaX;
            state.centerY = state.panStartCenterY - boardDeltaY;

            applyCameraTransform(state);
        };

        state.onPanEnd = () => {
            if (!state.isPanning) {
                return;
            }

            state.isPanning = false;
            element.classList.remove("panning");

            if (state.panMoved) {
                suppressClickAfterPan(state);
            }

            syncViewportToDotNet(state);
        };

        // Touch: one finger pans like a mouse drag, two fingers pinch to zoom around the point between them.
        function startTouchPan(touch) {
            state.onMouseDown({ button: 0, target: touch.target, clientX: touch.clientX, clientY: touch.clientY, preventDefault() { } });
        }

        function startPinch(first, second) {
            const midX = (first.clientX + second.clientX) / 2;
            const midY = (first.clientY + second.clientY) / 2;
            const relativePoint = getRelativePoint(element, midX, midY);
            const width = Math.max(1, relativePoint.width);
            const height = Math.max(1, relativePoint.height);
            const zoomScale = getZoomScale(state.zoomPercent);
            const viewWidth = state.mapWidth / zoomScale;
            const viewHeight = state.mapHeight / zoomScale;

            state.pinch = {
                startDistance: Math.max(1, Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY)),
                startZoom: state.zoomPercent,
                // The board point under the fingers stays under them as they move apart or together.
                boardX: state.centerX - viewWidth / 2 + (relativePoint.x / width) * viewWidth,
                boardY: state.centerY - viewHeight / 2 + (relativePoint.y / height) * viewHeight
            };
        }

        function movePinch(first, second) {
            const midX = (first.clientX + second.clientX) / 2;
            const midY = (first.clientY + second.clientY) / 2;
            const relativePoint = getRelativePoint(element, midX, midY);
            const width = Math.max(1, relativePoint.width);
            const height = Math.max(1, relativePoint.height);
            const distance = Math.max(1, Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY));
            const nextZoom = clamp(state.pinch.startZoom * distance / state.pinch.startDistance, state.minZoom, state.maxZoom);

            state.zoomPercent = nextZoom;
            if (nextZoom <= state.minZoom) {
                const center = getMapCenter(state);
                state.centerX = center.x;
                state.centerY = center.y;
                return;
            }

            const zoomScale = getZoomScale(nextZoom);
            const viewWidth = state.mapWidth / zoomScale;
            const viewHeight = state.mapHeight / zoomScale;
            state.centerX = state.pinch.boardX - (relativePoint.x / width) * viewWidth + viewWidth / 2;
            state.centerY = state.pinch.boardY - (relativePoint.y / height) * viewHeight + viewHeight / 2;
        }

        state.onTouchStart = event => {
            if (event.target && event.target.closest("[data-mapboard-pan-ignore='true']")) {
                return;
            }

            if (event.touches.length >= 2) {
                state.isPanning = false;
                startPinch(event.touches[0], event.touches[1]);
                event.preventDefault();
            } else if (event.touches.length === 1) {
                state.pinch = null;
                startTouchPan(event.touches[0]);
            }
        };

        state.onTouchMove = event => {
            if (state.pinch && event.touches.length >= 2) {
                event.preventDefault();
                movePinch(event.touches[0], event.touches[1]);
                state.panMoved = true;
                applyCameraTransform(state);
            } else if (state.isPanning && event.touches.length === 1) {
                // Keep the page from scrolling or zooming while the map pans.
                event.preventDefault();
                state.onMouseMove(event.touches[0]);
            }
        };

        state.onTouchEnd = event => {
            if (state.pinch) {
                if (event.touches.length >= 2) {
                    return;
                }

                state.pinch = null;
                suppressClickAfterPan(state);
                if (event.touches.length === 1) {
                    // Lifting one finger carries on as a pan with the other.
                    startTouchPan(event.touches[0]);
                } else {
                    syncViewportToDotNet(state);
                }

                return;
            }

            if (event.touches.length === 0) {
                state.onPanEnd();
            }
        };

        state.onClickCapture = event => {
            if (performance.now() >= state.suppressClickUntil) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
        };

        element.addEventListener("wheel", state.onWheel, { passive: false });
        element.addEventListener("mousedown", state.onMouseDown);
        window.addEventListener("mousemove", state.onMouseMove);
        window.addEventListener("mouseup", state.onPanEnd);
        element.addEventListener("mouseleave", state.onPanEnd);
        element.addEventListener("click", state.onClickCapture, true);
        element.addEventListener("touchstart", state.onTouchStart, { passive: false });
        element.addEventListener("touchmove", state.onTouchMove, { passive: false });
        element.addEventListener("touchend", state.onTouchEnd);
        element.addEventListener("touchcancel", state.onTouchEnd);
    }

    function detachHandlers(state) {
        const element = state.element;

        element.removeEventListener("wheel", state.onWheel);
        element.removeEventListener("mousedown", state.onMouseDown);
        window.removeEventListener("mousemove", state.onMouseMove);
        window.removeEventListener("mouseup", state.onPanEnd);
        element.removeEventListener("mouseleave", state.onPanEnd);
        element.removeEventListener("click", state.onClickCapture, true);
        element.removeEventListener("touchstart", state.onTouchStart);
        element.removeEventListener("touchmove", state.onTouchMove);
        element.removeEventListener("touchend", state.onTouchEnd);
        element.removeEventListener("touchcancel", state.onTouchEnd);
        element.classList.remove("panning");

        if (state.syncHandle) {
            clearTimeout(state.syncHandle);
            state.syncHandle = null;
        }

    }

    function initializeCamera(element, options, dotNetReference) {
        if (!element) {
            return;
        }

        const cameraGroup = element.querySelector(".game-map-camera") || element.querySelector(".map-board-camera");
        if (!cameraGroup) {
            return;
        }

        let state = states.get(element);
        if (!state) {
            state = {
                element,
                cameraGroup,
                dotNetReference,
                minZoom: options.minZoom,
                maxZoom: options.maxZoom,
                mapLeft: options.mapLeft,
                mapTop: options.mapTop,
                mapWidth: options.mapWidth,
                mapHeight: options.mapHeight,
                zoomPercent: options.zoomPercent,
                centerX: options.centerX,
                centerY: options.centerY,
                isPanning: false,
                panMoved: false,
                suppressClickUntil: 0,
                panStartClientX: 0,
                panStartClientY: 0,
                panStartCenterX: options.centerX,
                panStartCenterY: options.centerY,
                viewportSequence: 0,
                syncHandle: null,
                pinch: null
            };

            installHandlers(state);
            states.set(element, state);
        } else {
            state.cameraGroup = cameraGroup;
            state.dotNetReference = dotNetReference;
            state.minZoom = options.minZoom;
            state.maxZoom = options.maxZoom;
            state.mapLeft = options.mapLeft;
            state.mapTop = options.mapTop;
            state.mapWidth = options.mapWidth;
            state.mapHeight = options.mapHeight;
            state.zoomPercent = options.zoomPercent;
            state.centerX = options.centerX;
            state.centerY = options.centerY;
        }

        applyCameraTransform(state);
    }

    function disposeCamera(element) {
        if (!element) {
            return;
        }

        const state = states.get(element);
        if (!state) {
            return;
        }

        detachHandlers(state);
        states.delete(element);
    }

    function stopHeartbeat(key) {
        const existing = heartbeats.get(key);
        if (!existing) {
            return;
        }

        clearInterval(existing.intervalHandle);
        document.removeEventListener("visibilitychange", existing.onVisibilityChange);
        heartbeats.delete(key);
    }

    function startHeartbeat(key, dotNetReference, intervalMilliseconds) {
        if (!key || !dotNetReference) {
            return;
        }

        stopHeartbeat(key);

        const sendHeartbeat = () => {
            dotNetReference.invokeMethodAsync("SendPresenceHeartbeat")
                .catch(() => {
                    stopHeartbeat(key);
                });
        };

        const onVisibilityChange = () => {
            if (document.visibilityState === "visible") {
                sendHeartbeat();
            }
        };

        const intervalHandle = window.setInterval(sendHeartbeat, Math.max(1000, intervalMilliseconds || 5000));
        document.addEventListener("visibilitychange", onVisibilityChange);
        heartbeats.set(key, { intervalHandle, onVisibilityChange });
        sendHeartbeat();
    }

    window.mapBoard = {
        getRelativePoint,
        initializeCamera,
        disposeCamera
    };

    window.mapBoardPresence = {
        startHeartbeat,
        stopHeartbeat
    };
})();
