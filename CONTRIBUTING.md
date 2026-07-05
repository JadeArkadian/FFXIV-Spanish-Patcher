# Contributing

Thank you for considering a contribution to FFXIVSpanish Patcher.

This project accepts contributions to the application source code, documentation, tooling, localization workflow, terminology, and Spanish translation quality.

By submitting a contribution through a pull request, issue, patch, commit, or any other form, you agree to the terms below.

## Code Contributions

Unless explicitly stated otherwise, contributions to the application source code, build scripts, project files, and technical documentation are provided under the MIT License used by this repository.

This repository uses Git LFS for `data/translations.dat`, the Brotli-compressed translation blob embedded by the app. Before building, testing, or publishing from a fresh clone, make sure Git LFS has downloaded the real blob:

```bash
git lfs pull
```

Without that, builds may embed the small LFS pointer text instead of the `.dat` payload, and translation loading will fail at runtime/test time.

By contributing code, you confirm that:

* You have the right to submit the contribution.
* Your contribution does not knowingly include code you are not allowed to share.
* Your contribution may be used, copied, modified, distributed, sublicensed, and published under the MIT License.

## Localization and Translation Contributions

Spanish translations, localization edits, terminology suggestions, glossaries, translation memories, review notes, and other linguistic contributions are treated separately from source code.

By contributing localization or translation content to this project, you confirm that:

* You have the right to submit the contribution.
* Your contribution is your own original work, or you otherwise have the necessary rights to submit it.
* Your contribution does not knowingly include unauthorized third-party copyrighted material beyond what is necessary for interoperability, reference, review, or lawful localization work.
* You grant the project maintainer a worldwide, perpetual, irrevocable, royalty-free, sublicensable, and transferable license to use, reproduce, modify, adapt, publish, distribute, display, include in mod packages, and otherwise exploit your contribution as part of this project or related localization efforts.

This license grant is intended to allow the project to continue operating, distributing localization work, accepting future maintainers, and entering into potential commercial, official, or partnership arrangements related to the localization work.

Unless explicitly stated otherwise, localization and translation contributions are not licensed under the MIT License.

## Generated or AI-Assisted Contributions

AI-assisted contributions are allowed only if the contributor has reviewed them and accepts responsibility for them.

By submitting AI-assisted content, you confirm that:

* You have reviewed and edited the contribution before submission.
* You believe the contribution is suitable for inclusion in the project.
* You are not knowingly submitting text, code, translations, or other material copied from unauthorized sources.
* You have not included secrets, credentials, private data, or confidential information in the contribution.
* You grant the project the same rights over the AI-assisted contribution as you would for a human-authored contribution.
* You accept the terms described in `AI_USAGE.md`.

AI tools are considered assistants only. They are not treated as authors, maintainers, contributors, or copyright holders of this project.

The project maintainer may request clarification about AI-assisted contributions and may reject, edit, rewrite, remove, or replace AI-assisted material if there are concerns about quality, licensing, originality, security, maintainability, or project direction.

## No Square Enix Ownership Claim

Contributors do not claim ownership over Final Fantasy XIV, Square Enix assets, original game text, trademarks, characters, locations, or other Square Enix intellectual property.

This project only claims rights over original code, tooling, Spanish translation work, terminology decisions, glossaries, notes, and other original contributions authored for this project.

## Contributor Credit

Contributors may be credited in project documentation, release notes, or other project materials.

A contribution does not create employment, ownership, partnership, endorsement, or any right to control the project.

## Project Maintainer Rights

The project maintainer may accept, reject, edit, reorganize, remove, relicense, or stop distributing contributions as needed for the project.

The project maintainer may also change the licensing model for future versions of the project, provided that previously distributed material remains subject to the terms under which it was originally distributed.
