const catalog = require("../../Mzying2001.MonkeySharp.Core/Js/ApiCatalog.json");

// Most operation tests exercise both calling forms. Explicit exposure tests
// always provide their own declaredGrants; no production fallback is involved.
module.exports = function completePlan(payload) {
    if (!payload || !Array.isArray(payload.invocations)) return payload;
    for (const invocation of payload.invocations) {
        if (!Object.hasOwn(invocation, "declaredGrants")) {
            invocation.declaredGrants = (invocation.grants || []).flatMap(name => {
                const legacy = catalog.find(api => api.capability === name)?.legacy;
                return legacy ? [name, legacy] : [name];
            });
        }
        const capabilities = new Set(invocation.grants || []);
        for (const descriptor of catalog) {
            if (capabilities.has(descriptor.capability)) {
                for (const member of catalog.filter(api => api.family === descriptor.family)) capabilities.add(member.capability);
            }
        }
        invocation.grants = [...capabilities];
    }
    return payload;
};
