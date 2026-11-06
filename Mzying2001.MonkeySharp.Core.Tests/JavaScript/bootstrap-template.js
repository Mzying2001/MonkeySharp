const fs = require("node:fs");
const path = require("node:path");
const directory = path.resolve(__dirname, "../../Mzying2001.MonkeySharp.Core/Js");
let source = fs.readFileSync(path.join(directory, "Init.js"), "utf8");
for (const name of ["Storage", "Network", "HostInteraction", "Page"]) {
    source = source.replace(`/*__MONKEYSHARP_${name.toUpperCase()}_MODULE__*/`, fs.readFileSync(path.join(directory, name + ".js"), "utf8"));
}
module.exports = source.replace("__MONKEYSHARP_API_CATALOG__", fs.readFileSync(path.join(directory, "ApiCatalog.json"), "utf8"));
