(() => {
    // The longest a sound may hold up the next one, in case the browser never says it finished.
    const maxWaitMilliseconds = 10000;

    // One sound at a time: a roll that also starts the next turn plays each in order rather than over each other.
    let queue = Promise.resolve();

    // Sounds still playing, kept so the browser doesn't drop them part way through.
    const playing = new Set();

    function playOne(url) {
        return new Promise(resolve => {
            const audio = new Audio(url);
            let timer = 0;
            const finish = () => {
                clearTimeout(timer);
                playing.delete(audio);
                resolve();
            };

            playing.add(audio);
            audio.addEventListener("ended", finish, { once: true });
            audio.addEventListener("error", () => {
                console.warn("gameSounds: couldn't load", url);
                finish();
            }, { once: true });
            timer = setTimeout(finish, maxWaitMilliseconds);

            // Browsers refuse sound until the player has clicked on the page; there's nothing to do but skip it.
            audio.play().catch(error => {
                console.warn("gameSounds: couldn't play", url, error?.name);
                finish();
            });
        });
    }

    window.gameSounds = {
        play(urls) {
            // A single URL is one sound, not a list of its characters.
            const list = typeof urls === "string" ? [urls] : Array.from(urls ?? []);
            console.debug("gameSounds: play", list.map(url => url.startsWith("data:") ? "data:..." : url));
            for (const url of list) {
                queue = queue.then(() => playOne(url)).catch(() => {});
            }
        }
    };
})();
