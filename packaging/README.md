# Packaging

## Windows

```
dotnet publish src/Maxwell.App -c Release -f net10.0-windows10.0.17763.0 -r win-x64 -p:PublishSingleFile=true -o Deploys
```

Produces `Deploys/Maxwell.exe` — one self-contained file, ~169 MB, that runs without a
.NET install. The content pane's HTML, CSS and scripts are embedded in it too, so
nothing else lands beside the exe. `PublishSingleFile` also switches on self-extraction of the native Skia
and HarfBuzz libraries; without that they land beside the exe and the single file is
not single. The natives unpack once per version into `%TEMP%\.net`, so first start is
slower than later ones.

Bundle compression is deliberately not used; it cut the file from 134 MB to ~55 MB when
it was last measured. A compressed
assembly cannot be mapped from the bundle, so the runtime decompresses it into private
memory: measured idle in the tray, 86 MB private working set compressed against 47 MB
uncompressed, and twice the startup CPU. Add
`-p:EnableCompressionInSingleFile=true` if the download size ever matters more than
the resident cost.

Drop `-p:PublishSingleFile=true` for a plain folder publish. Trimming is deliberately
off in both: Avalonia resolves controls and styles through reflection, and a trimmed
build fails at runtime rather than at publish time.

Nothing else needs installing, and the app writes only to
`%APPDATA%\Maxwell.App\settings.json`.

## Linux

```
dotnet publish src/Maxwell.App -c Release -f net10.0 -r linux-x64 --self-contained
sudo cp packaging/linux/99-maxwell-app.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
```

The udev rule is not optional. Without it the hidraw node is root-only and the app
reports no device at all, which is indistinguishable from a fault.

Notifications go through `notify-send`, so `libnotify` must be present. It ships with
every mainstream desktop; without it notifications are skipped and nothing else breaks.

Global shortcuts are not implemented on Linux — the hotkeys screen reports every
binding as unavailable rather than pretending they work.

## macOS

Not supported yet. The HID transport needs an IOKit implementation that has not been
written; the app starts and reports no device. See `UnsupportedPlatform.cs`.
