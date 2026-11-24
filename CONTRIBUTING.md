# Contributing to MonkeySharp

**English** | [中文](CONTRIBUTING.zh-CN.md)

## Open an issue

Search existing issues first. Use **Bug report** for unexpected behavior in MonkeySharp, **Compatibility report** to compare userscript managers or CefSharp environments, and **Feature request** to propose an improvement. [Open a new issue](https://github.com/Mzying2001/MonkeySharp/issues/new/choose)

Include the shortest steps that show the problem and a minimal userscript or host example if available. For userscripts, keep the metadata and page conditions that affect reproduction. Remove credentials and private data from examples and logs before sharing them.

## Submit a pull request

1. Fork the repository and create a working branch from `dev`. Target `dev` when opening the PR. If a maintainer requests a specific branch, use that branch as the base.
2. Keep the PR focused. For behavior changes, update the relevant tests and documentation. Keep the English and Chinese guides aligned when updating an existing pair. [Documentation index](docs/README.md)
3. Use Conventional Commits with a concise English summary, for example `fix(core): handle missing resource metadata`. For non-trivial changes, explain why and how in the commit body.
4. Fill in the PR template with the reason for the change, any related issue, verification results, and relevant compatibility or merge risks.

## Choose verification for your change

- For Core changes, run the relevant .NET tests. For JavaScript bridge or GM API changes, run the relevant JavaScript tests. [Build and test](docs/development.md#build-and-test)
- For CefSharp changes, use an explicit `x64` or `x86` platform and keep the exact CefSharp version consistent across projects and checks. The WPF Demo and its tests use `x64`. For changes that affect browser behavior, run the relevant real-Chromium checks from a Windows desktop session. [Real-Chromium gates](docs/development.md#real-chromium-gates)
- For documentation-only changes, check links and anchors, and compare the instructions in both languages where applicable. [Documentation maintenance](docs/development.md#documentation-maintenance)

Record the checks you ran and their results in the PR. If a relevant check could not run, note the reason.
