# Contributing

Thanks for considering a contribution! This project is a community launcher
for *Age of Empires III* total-conversion mods — bug reports, new mod
profiles, translations and feature work are all welcome.

## Quick checklist before opening a PR

1. **One topic per PR.** Keep refactors out of feature PRs.
2. **Run the launcher locally** (`dotnet build -c Release` in
   `WarsOfLibertyLauncher/`) and exercise the [smoke test](docs/ROADMAP.md#smoke-test-run-after-every-ui-commit-during-v08).
3. **Follow the existing style.** The codebase uses file-scoped namespaces,
   `Nullable` enabled, and brief doc-comments explaining *why* a piece of
   code exists. Don't add comments that just restate the code.
4. **Sign off every commit** (see DCO below). PRs whose commits aren't
   signed off will be asked to fix that before review.

## Developer Certificate of Origin (DCO)

Every commit you push must include a `Signed-off-by` line at the end of the
commit message:

```
Signed-off-by: Your Name <your.email@example.com>
```

By signing off, you certify that you wrote the patch yourself (or otherwise
have the right to submit it under the project's Apache-2.0 license), as
spelled out by the **[Developer Certificate of Origin, v1.1](https://developercertificate.org/)**:

> By making a contribution to this project, I certify that:
>
> (a) The contribution was created in whole or in part by me and I have
> the right to submit it under the open source license indicated in the file; or
>
> (b) The contribution is based upon previous work that, to the best of my
> knowledge, is covered under an appropriate open source license and I have
> the right under that license to submit that work with modifications,
> whether created in whole or in part by me, under the same open source
> license (unless I am permitted to submit under a different license),
> as indicated in the file; or
>
> (c) The contribution was provided directly to me by some other person who
> certified (a), (b) or (c) and I have not modified it.
>
> (d) I understand and agree that this project and the contribution are
> public and that a record of the contribution (including all personal
> information I submit with it, including my sign-off) is maintained
> indefinitely and may be redistributed consistent with this project or
> the open source license(s) involved.

`git commit -s` adds the line automatically. If you forget, an interactive
rebase + `git commit --amend -s` on each commit (or `git rebase --signoff`)
will fix it.

## Licensing

By submitting a contribution you agree that it is licensed under the
[Apache License 2.0](LICENSE), the same license as the rest of the project.
The Apache 2.0 license grants explicit patent rights from contributors,
which is why we prefer it over MIT for this project.

We do **not** require a separate CLA. The DCO sign-off above is sufficient.

## Adding a new mod profile

Don't edit `ModRegistry._builtIn` for community mods. Open a PR against the
[community catalog repo](https://github.com/Gorgorito12/aoe3-mods-catalog)
instead — the launcher will pick it up automatically. The in-app
**"Publish my mod"** wizard (Mods tab → Publish) generates a valid
`mod.json` against the embedded JSON schema and opens a pre-filled PR for
you.

## Reporting bugs

- Reproduction steps, expected vs. actual, OS version, and the launcher
  version from the title bar.
- Attach the launcher log if relevant: open the settings gear → **Advanced
  → View diagnostic logs**. The log stays in English on purpose so it can
  be searched.

## Code of conduct

Be civil, be patient, and assume good faith. Maintainers may remove
comments or close PRs that derail discussion.
