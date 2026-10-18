// ===== Tiện ích chung =====
function afToken() { return document.querySelector('#af-form input[name="__RequestVerificationToken"]').value; }

function showToast(msg) {
    document.getElementById('toast-body').textContent = msg;
    bootstrap.Toast.getOrCreateInstance(document.getElementById('toast'), { delay: 2200 }).show();
}

// ===== Thêm món vào giỏ (modal chọn số lượng + ghi chú) =====
(function () {
    const modalEl = document.getElementById('addModal');
    if (!modalEl) return;
    const modal = new bootstrap.Modal(modalEl);
    const qtyEl = document.getElementById('add-qty');
    const noteEl = document.getElementById('add-note');
    let current = null;

    document.querySelectorAll('[data-add-product]').forEach(btn => btn.addEventListener('click', () => {
        current = btn.dataset.addProduct;
        document.getElementById('add-name').textContent = btn.dataset.name;
        document.getElementById('add-price').textContent = btn.dataset.price;
        qtyEl.value = 1; noteEl.value = '';
        modal.show();
    }));
    document.getElementById('add-minus').onclick = () => qtyEl.value = Math.max(1, +qtyEl.value - 1);
    document.getElementById('add-plus').onclick = () => qtyEl.value = Math.min(50, +qtyEl.value + 1);
    modalEl.querySelectorAll('[data-note]').forEach(c => c.addEventListener('click', () => {
        const t = c.dataset.note;
        noteEl.value = noteEl.value ? noteEl.value + ', ' + t : t;
    }));

    document.getElementById('add-confirm').onclick = async () => {
        const body = new URLSearchParams({ productId: current, quantity: qtyEl.value, note: noteEl.value, __RequestVerificationToken: afToken() });
        const res = await fetch('/Cart/Add', { method: 'POST', headers: { 'X-Requested-With': 'XMLHttpRequest' }, body });
        modal.hide();
        if (!res.ok) { showToast('Không thêm được món này.'); return; }
        const d = await res.json();
        document.getElementById('cart-badge').textContent = d.count;
        showToast('Đã thêm "' + d.name + '" vào giỏ');
        const fc = document.getElementById('floating-cart');
        if (fc) { fc.classList.remove('d-none'); fc.querySelector('span').textContent = d.count + ' món · ' + d.total.toLocaleString('vi-VN') + 'đ'; }
    };
})();

// ===== Chọn voucher từ ví =====
document.querySelectorAll('[data-voucher-code]').forEach(el => el.addEventListener('click', () => {
    const input = document.getElementById('VoucherCode') || document.querySelector('input[name="voucherCode"]');
    if (input) input.value = el.dataset.voucherCode;
}));

// ===== Tự làm mới trang nhân viên (sơ đồ bàn, hàng đợi đơn) =====
// Realtime: staff-alerts.js phát sự kiện "oc:changed" mỗi khi server đẩy SSE. Dự phòng 60 giây/lần.
(function () {
    const el = document.querySelector('[data-autorefresh]');
    if (!el) return;
    let pending = null;
    function tryReload() {
        // Không reload khi nhân viên đang gõ vào ô nhập hoặc đang mở hộp thoại -> thử lại sau
        if ((document.activeElement && /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) || document.querySelector('.modal.show')) {
            pending = setTimeout(tryReload, 3000);
            return;
        }
        location.reload();
    }
    window.addEventListener('oc:changed', () => { clearTimeout(pending); pending = setTimeout(tryReload, 500); });
    setInterval(tryReload, 60000);
})();

// ===== Trang khách (theo dõi đơn, hóa đơn bàn): SSE, dự phòng polling 30 giây =====
(function () {
    const el = document.querySelector('[data-sse-url]') || document.querySelector('[data-poll-url]');
    if (!el) return;
    let timer = null;
    function reloadSoon() {
        clearTimeout(timer);
        timer = setTimeout(function tryReload() {
            if (document.activeElement && /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) { timer = setTimeout(tryReload, 3000); return; }
            location.reload();
        }, 400);
    }
    if (el.dataset.sseUrl && 'EventSource' in window) {
        const es = new EventSource(el.dataset.sseUrl);
        let first = true;
        es.addEventListener('changed', reloadSoon);
        es.onopen = () => { if (!first) reloadSoon(); first = false; };   // kết nối lại -> bù thay đổi bị lỡ
        window.addEventListener('beforeunload', () => es.close());
    }
    if (el.dataset.pollUrl) {
        let last = el.dataset.pollState;
        setInterval(async () => {
            try {
                const d = await (await fetch(el.dataset.pollUrl)).json();
                const state = d.status ?? JSON.stringify(d);
                if (last === undefined) last = state; else if (state !== last) location.reload();
            } catch { }
        }, 30000);
    }
})();
