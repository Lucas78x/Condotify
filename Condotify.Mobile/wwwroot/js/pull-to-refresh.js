// Whole-page pull-to-refresh: the app scrolls the document itself (no inner
// scroll container), so the gesture listens on the document and only arms
// when the page is already at the top.
window.condotifyPullToRefresh = {
    _dotNetRef: null,
    _indicator: null,
    _startY: 0,
    _lastPull: 0,
    _pulling: false,
    _busy: false,
    _threshold: 70,

    attach(dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._indicator = document.getElementById('ptr-indicator');
        document.addEventListener('touchstart', this._onStart.bind(this), { passive: true });
        document.addEventListener('touchmove', this._onMove.bind(this), { passive: false });
        document.addEventListener('touchend', this._onEnd.bind(this));
        document.addEventListener('touchcancel', this._onEnd.bind(this));
    },

    detach() {
        this._dotNetRef = null;
        this._reset();
    },

    _atTop() {
        return (document.scrollingElement?.scrollTop || 0) <= 0;
    },

    _onStart(event) {
        if (this._busy || !this._atTop() || event.touches.length !== 1) { this._pulling = false; return; }
        this._startY = event.touches[0].clientY;
        this._pulling = true;
    },

    _onMove(event) {
        if (!this._pulling) return;
        const dy = event.touches[0].clientY - this._startY;
        if (dy <= 0 || !this._atTop()) { this._reset(); return; }
        event.preventDefault();
        const pull = Math.min(dy * 0.5, 110);
        this._lastPull = pull;
        if (this._indicator) {
            this._indicator.style.opacity = String(Math.min(pull / this._threshold, 1));
            this._indicator.style.transform = `translate(-50%, ${pull - 40}px) rotate(${pull * 2.5}deg)`;
            this._indicator.classList.toggle('ptr-ready', pull >= this._threshold);
        }
    },

    _onEnd() {
        if (!this._pulling) return;
        this._pulling = false;
        const pull = this._lastPull;
        if (pull >= this._threshold && this._dotNetRef) {
            this._busy = true;
            if (this._indicator) {
                this._indicator.style.opacity = '1';
                this._indicator.style.transform = 'translate(-50%, 20px)';
                this._indicator.classList.add('ptr-loading');
            }
            this._dotNetRef.invokeMethodAsync('OnPullToRefreshAsync').finally(() => this._finish());
        } else {
            this._reset();
        }
    },

    _finish() {
        this._busy = false;
        this._reset();
    },

    _reset() {
        this._pulling = false;
        this._lastPull = 0;
        if (this._indicator) {
            this._indicator.style.opacity = '0';
            this._indicator.style.transform = 'translate(-50%, -40px) rotate(0deg)';
            this._indicator.classList.remove('ptr-ready', 'ptr-loading');
        }
    }
};
