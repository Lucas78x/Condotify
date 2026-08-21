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
    _attached: false,
    _boundStart: null,
    _boundMove: null,
    _boundEnd: null,

    attach(dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._indicator = document.getElementById('ptr-indicator');
        if (this._attached) return;
        this._boundStart = this._onStart.bind(this);
        this._boundMove = this._onMove.bind(this);
        this._boundEnd = this._onEnd.bind(this);
        document.addEventListener('touchstart', this._boundStart, { passive: true });
        document.addEventListener('touchmove', this._boundMove, { passive: false });
        document.addEventListener('touchend', this._boundEnd);
        document.addEventListener('touchcancel', this._boundEnd);
        this._attached = true;
    },

    detach() {
        if (this._attached) {
            document.removeEventListener('touchstart', this._boundStart);
            document.removeEventListener('touchmove', this._boundMove);
            document.removeEventListener('touchend', this._boundEnd);
            document.removeEventListener('touchcancel', this._boundEnd);
        }
        this._attached = false;
        this._boundStart = null;
        this._boundMove = null;
        this._boundEnd = null;
        this._dotNetRef = null;
        this._reset();
    },

    _atTop() {
        return (document.scrollingElement?.scrollTop || 0) <= 0;
    },

    _onStart(event) {
        if (this._busy || !this._atTop() || event.touches.length !== 1) { this._pulling = false; return; }
        // The authenticated shell may appear after the first render while a
        // stored session is being restored. Resolve the indicator lazily so
        // the gesture still has visual feedback in that flow.
        this._indicator = document.getElementById('ptr-indicator');
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
