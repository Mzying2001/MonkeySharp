    const createLifecycleEvents = function (documentId) {
        const root = globalThis;
        const now = () => root.performance && root.performance.now ? root.performance.now() : Date.now();
        const moments = { DocumentStart: 0 };
        const events = new Map();
        let ordinal = 0;
        let disposed = false;
        const record = function (event) {
            if (disposed) return;
            const order = ++ordinal;
            if (event.type === "DOMContentLoaded" && moments.DocumentEnd === undefined) {
                moments.DocumentEnd = order;
                moments.DocumentIdle = order + 0.5;
            }
            if (!events.has(event.type)) events.set(event.type, []);
            const items = events.get(event.type);
            items.push({ original: event, order, time: now() });
            if (items.length > 256) items.shift();
        };
        const types = ["DOMContentLoaded", "load", "DOMNodeInserted"];
        types.forEach(type => root.addEventListener(type, record, true));
        const navigation = root.performance && root.performance.getEntriesByType && root.performance.getEntriesByType("navigation")[0];
        if (document.body) moments.DocumentBody = ordinal;
        if (navigation ? navigation.domContentLoadedEventStart > 0 : document.readyState === "complete") record(new Event("DOMContentLoaded"));
        if (navigation ? navigation.loadEventStart > 0 : document.readyState === "complete") record(new Event("load"));
        return Object.freeze({
            documentId,
            body() { if (moments.DocumentBody === undefined) moments.DocumentBody = ordinal; },
            snapshot(type, runAt, invocationMoment) {
                const moment = moments[runAt];
                return (events.get(type) || []).filter(item => moment === undefined ? item.time >= invocationMoment : item.order >= moment).map(item => {
                    const event = new Event(type, { bubbles: item.original.bubbles, cancelable: item.original.cancelable });
                    Object.defineProperty(event, "target", { value: item.original.target || document });
                    return { original: item.original, event };
                });
            },
            dispose() { disposed = true; types.forEach(type => root.removeEventListener(type, record, true)); events.clear(); }
        });
    };
