// Trung tâm thông báo cho nhân viên.
//  - Hỏi server mỗi 5 giây (/Staff/Alerts).
//  - Chuông hiện số việc cần xử lý; bấm vào xổ ra: "Cần xử lý ngay" (trực tiếp từ server)
//    và "Mới nhận" (lịch sử lưu ở localStorage, có đánh dấu chưa đọc).
//  - Có thông báo mới: phát âm báo (nếu bật), popup ngắn, chuông rung, số trên tiêu đề tab.
(function () {
    const POLL_MS = 5000;
    const SEEN_KEY = 'oc_alert_seen';   // sessionStorage: các thông báo tab này đã thấy
    const HIST_KEY = 'oc_notifs';       // localStorage: lịch sử, dùng chung giữa các tab
    const SOUND_KEY = 'oc_alert_sound';
    const MAX_HISTORY = 30;
    const baseTitle = document.title;

    const $ = id => document.getElementById(id);
    const bell = $('alert-bell'), badge = $('alert-count'), toggle = $('alert-toggle'), clearBtn = $('alert-clear');
    const liveBox = $('alert-live'), histBox = $('alert-history'), stack = $('alert-stack');
    let audio = null, live = [], menuOpen = false;

    const money = n => Number(n || 0).toLocaleString('vi-VN') + 'đ';
    const hhmm = t => new Date(t).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
    const soundOn = () => { try { return localStorage.getItem(SOUND_KEY) === '1'; } catch { return false; } };
    const load = (store, key, def) => { try { return JSON.parse(store.getItem(key)) ?? def; } catch { return def; } };
    const save = (store, key, v) => { try { store.setItem(key, JSON.stringify(v)); } catch { } };

    // ---- âm báo ----
    function beep() {
        if (!soundOn()) return;
        try {
            audio = audio || new (window.AudioContext || window.webkitAudioContext)();
            [880, 1175].forEach((f, i) => {
                const o = audio.createOscillator(), g = audio.createGain();
                o.type = 'sine'; o.frequency.value = f;
                o.connect(g); g.connect(audio.destination);
                const t = audio.currentTime + i * 0.18;
                g.gain.setValueAtTime(0.0001, t);
                g.gain.exponentialRampToValueAtTime(0.35, t + 0.02);
                g.gain.exponentialRampToValueAtTime(0.0001, t + 0.16);
                o.start(t); o.stop(t + 0.17);
            });
        } catch { }
    }

    function refreshToggle() {
        toggle.textContent = soundOn() ? '🔔 Âm báo: bật' : '🔕 Bật âm báo';
        toggle.classList.toggle('btn-warning', !soundOn());
        toggle.classList.toggle('btn-outline-secondary', soundOn());
    }
    toggle.addEventListener('click', () => {
        try { localStorage.setItem(SOUND_KEY, soundOn() ? '0' : '1'); } catch { }
        refreshToggle();
        if (soundOn()) {
            beep(); // click của người dùng nên trình duyệt cho phép phát âm
            if ('Notification' in window && Notification.permission === 'default') Notification.requestPermission();
        }
    });
    refreshToggle();

    // ---- dựng giao diện dropdown ----
    function item(text, href, opts = {}) {
        const a = document.createElement('a');
        a.className = 'alert-item' + (opts.unread ? ' unread' : '');
        a.href = href;
        a.appendChild(document.createTextNode(text));   // textContent: không chèn HTML từ dữ liệu khách
        if (opts.sub) { const s = document.createElement('small'); s.textContent = opts.sub; a.appendChild(s); }
        return a;
    }
    function section(title) { const d = document.createElement('div'); d.className = 'alert-section'; d.textContent = title; return d; }
    function empty(text) { const d = document.createElement('div'); d.className = 'alert-empty'; d.textContent = text; return d; }

    function render() {
        liveBox.replaceChildren(section('Cần xử lý ngay (' + live.length + ')'));
        if (!live.length) liveBox.appendChild(empty('Không có việc nào đang chờ 🎉'));
        live.forEach(x => liveBox.appendChild(item(x.text, x.href)));

        const hist = load(localStorage, HIST_KEY, []);
        histBox.replaceChildren(section('Mới nhận'));
        if (!hist.length) histBox.appendChild(empty('Chưa có thông báo nào.'));
        hist.forEach(h => histBox.appendChild(item(h.text, h.href, { unread: !h.read, sub: hhmm(h.t) })));

        const total = live.length;
        badge.textContent = total;
        badge.classList.toggle('d-none', total === 0);
        document.title = total > 0 ? '(' + total + ') ' + baseTitle : baseTitle;
        bell.classList.toggle('bell-ring', !menuOpen && hist.some(h => !h.read));
    }

    // mở chuông: hiển thị chưa đọc rồi đánh dấu đã đọc
    bell.addEventListener('show.bs.dropdown', () => {
        menuOpen = true;
        render();
        const hist = load(localStorage, HIST_KEY, []);
        hist.forEach(h => h.read = true);
        save(localStorage, HIST_KEY, hist);
        bell.classList.remove('bell-ring');
    });
    bell.addEventListener('hidden.bs.dropdown', () => { menuOpen = false; render(); });
    clearBtn.addEventListener('click', () => { save(localStorage, HIST_KEY, []); render(); });

    // ---- popup ngắn (lưu ở sessionStorage để không mất khi trang tự reload) ----
    const TOAST_KEY = 'oc_alert_toasts', TOAST_MS = 12000;
    function showToast(t) {
        if (!stack) return;
        const left = t.until - Date.now();
        if (left <= 0) return;
        const el = document.createElement('a');
        el.href = t.href; el.className = 'alert-toast'; el.textContent = t.text;
        stack.appendChild(el);
        setTimeout(() => el.remove(), left);
    }
    function popup(text, href) {
        const t = { text, href, until: Date.now() + TOAST_MS };
        save(sessionStorage, TOAST_KEY, [...load(sessionStorage, TOAST_KEY, []).filter(x => x.until > Date.now()), t]);
        showToast(t);
        if ('Notification' in window && Notification.permission === 'granted' && document.hidden) {
            try { const n = new Notification('Order Coffee', { body: text }); n.onclick = () => { window.focus(); location.href = href; }; } catch { }
        }
    }
    load(sessionStorage, TOAST_KEY, []).forEach(showToast);

    function addHistory(key, text, href) {
        const hist = load(localStorage, HIST_KEY, []);
        if (hist.some(h => h.key === key)) return false;          // tab khác đã ghi rồi
        hist.unshift({ key, text, href, t: Date.now(), read: false });
        save(localStorage, HIST_KEY, hist.slice(0, MAX_HISTORY));
        return true;
    }

    // ---- hỏi server ----
    async function poll() {
        let d;
        try {
            const res = await fetch('/Staff/Alerts', { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) return;
            d = await res.json();
        } catch { return; }

        const current = [];   // { key, text, href }
        d.online.forEach(o => current.push({ key: 'o' + o.id, href: '/Staff/Online', text: '🛵 Đơn online #' + o.id + ' · ' + (o.phone || '') + ' · ' + money(o.total) }));
        d.items.forEach(i => current.push({ key: 'i' + i.tableId + ':' + i.maxItemId, href: '/Staff/Table/' + i.tableId, text: '🪑 ' + i.name + ': ' + i.count + ' món chưa ra' }));
        d.payments.forEach(p => current.push({ key: 'p' + p.orderId, href: '/Staff/Table/' + p.tableId, text: '💳 ' + p.name + ' gọi thanh toán · ' + money(p.total) }));
        live = current;

        const seen = load(sessionStorage, SEEN_KEY, null);
        if (seen) { // lần đầu mở tab chỉ ghi nhận, không báo những thứ đã có sẵn
            const fresh = current.filter(c => !seen.includes(c.key));
            const added = [];
            fresh.forEach(c => {
                const label = c.key[0] === 'i' ? '🪑 ' + c.text.slice(2).split(':')[0] + ' vừa gọi món' : c.text;
                if (addHistory(c.key, label, c.href)) added.push([label, c.href]);
            });
            if (added.length) { beep(); added.forEach(a => popup(a[0], a[1])); }
        }
        save(sessionStorage, SEEN_KEY, current.map(c => c.key));
        render();
    }

    // ---- realtime: server đẩy "changed" qua SSE, trình duyệt chỉ lấy ảnh chụp trạng thái khi có thay đổi ----
    let timer = null;
    const schedulePoll = () => { clearTimeout(timer); timer = setTimeout(poll, 200); };

    render();
    poll();
    if ('EventSource' in window) {
        const es = new EventSource('/Staff/Stream');           // tự kết nối lại khi mất mạng
        es.addEventListener('changed', () => {
            schedulePoll();
            window.dispatchEvent(new Event('oc:changed'));      // để trang (sơ đồ bàn, đơn online) tự làm mới
        });
        es.onopen = () => schedulePoll();                       // vừa kết nối lại -> bù những gì bị lỡ
        window.addEventListener('beforeunload', () => es.close());
    }
    setInterval(poll, POLL_MS * 12);                             // dự phòng 60 giây/lần nếu SSE bị chặn
})();
