# Contributing

Thanks for helping with Snapr.

## Before you start

- For a bug, open an [issue](https://github.com/teransarathchandra/snapr/issues/new/choose) with steps to reproduce and your Windows version. Logs are in `%APPDATA%\Snapr\logs\`.
- For a bigger change, open an issue first so we can agree on the approach.

## Develop

Needs Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet test                          # run the tests
dotnet run --project src/Snapr.App   # run the app
```

- `src/Snapr.Core` holds the model, file formats and image logic, with no UI. Put testable logic here.
- `src/Snapr.App` is the WPF app.
- `tests/Snapr.Tests` covers both. Add a test with every fix or feature.

## Pull requests

- Keep each PR to one change, and make sure `dotnet test` passes.
- Use a [conventional commit](https://www.conventionalcommits.org/) title, for example `fix: keep arrow colour after undo`.
- Snapr is written independently. Do not contribute code, images, icons, templates or text copied from any commercial product.

By contributing you agree that your work is released under the [MIT licence](LICENSE).
