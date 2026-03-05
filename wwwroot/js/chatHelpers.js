
window.chatHelpers = window.chatHelpers || {
    watchChatScrollById: function (id, dotNetHelper) {
        var element = document.getElementById(id);
        if (element) {
            element.addEventListener('scroll', function () {
                var isAtTop = element.scrollTop < 20;
                dotNetHelper.invokeMethodAsync('OnChatScroll', isAtTop);
            });
        }
    },

    ScrollToBottom: function (id) {
        var element = document.getElementById(id);
        if (element) {
            element.scrollTo({
                top: element.scrollHeight,
                behavior: 'smooth'
            });
        }
    },

    getSaveScrollInfo: function (id) {
        var el = document.getElementById(id);
        return el ? (el.scrollHeight - el.scrollTop) : 0;
    },

    restoreScrollPosition: function (id, scrollInfo) {
        var el = document.getElementById(id);
        if (el) {
            requestAnimationFrame(() => {
                el.scrollTop = el.scrollHeight - scrollInfo;
            });
        }
    }
};