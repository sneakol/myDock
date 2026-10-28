# Contributing to myDock

Thanks for the interest. Small, focused pull requests are the easiest to review.

## Build and test

You need the .NET 10 SDK and a running Docker Engine (the tests use a fake Docker service, so they do not need one).

```bash
dotnet build
dotnet test
dotnet run --project src/MyDock.App
```

The build treats warnings as errors. CI runs the same build and tests on every push and pull request.

## Workflow

- Branch from `develop` and open the pull request against `develop`. `main` only receives releases.
- Keep one change per pull request, with a short commit subject in the imperative ("Add ...", "Fix ...").
- Packages and releases are built by the maintainer only; pull requests do not need to touch `packaging/` or the version.

## Code

- `src/MyDock.Core` has no UI dependencies: put Docker, monitoring and formatting logic there and keep it testable.
- New logic comes with a test in `tests/MyDock.Core.Tests`. Fake the Docker side with `FakeDocker`.
- Follow the style of the surrounding code (naming, comment density, idiom).

## Translations

User-visible text lives in `src/MyDock.Core/Localization/Strings.resx` (English) and `Strings.it.resx` (Italian).
When you add or change a text, update both files. `LocalizationTests` checks that the two have the same keys and placeholders,
and that every key used in the code exists.

## Reporting problems

Use the issue templates. For a bug, include your distribution, the session type (X11 or Wayland) and the output of `mydock --dump`.
For security problems, do not open a public issue: see [SECURITY.md](SECURITY.md).

By contributing you agree that your work is released under the [MIT license](LICENSE).
