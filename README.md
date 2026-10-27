# myDock

A small "widget" for Ubuntu that shows the running Docker containers and the resources they use:
a tray icon with the container count, and a compact panel with the CPU, RAM, network and disk of
each one, plus the commands to start, stop and restart them.

Written in .NET 10 with [Avalonia](https://avaloniaui.net/).

## Requirements

- Ubuntu with GNOME (tested on 26.04, Wayland session) and the AppIndicator extension, enabled by default.
- A running Docker Engine.
- The user must be able to read `/var/run/docker.sock`:

  ```bash
  sudo usermod -aG docker $USER   # then log out and back in
  ```

  Members of the `docker` group effectively have root privileges on the machine.
- To build: the .NET 10 SDK (`sudo apt install dotnet-sdk-10.0`). The installed app does not need it.

## Installation

```bash
packaging/install.sh             # build and install for the current user
packaging/install.sh --uninstall # remove everything, except the settings
```

The app ends up in `~/.local/share/mydock`, with the `mydock` command in `~/.local/bin` and an entry in the
applications menu. To start it at login: tray icon menu → *Start at login*.

### Debian / Ubuntu package

```bash
packaging/build-deb.sh                      # builds dist/mydock_<version>_amd64.deb and _arm64.deb
sudo apt install ./dist/mydock_1.0.0_<arch>.deb   # amd64 or arm64, as per `dpkg --print-architecture`
sudo apt remove mydock
```

The package installs the app in `/opt/mydock`, the `mydock` command in `/usr/bin` and the menu entry and icon system-wide,
and pulls in the libraries it needs. It recommends Docker's CLI (compose and the shell button), `libnotify-bin`
(notifications) and the AppIndicator extension. Settings stay in `~/.config/mydock`. If you also used `install.sh`,
remove that copy first (`packaging/install.sh --uninstall`), or it shadows the packaged command. Quit a running
instance (`mydock --quit`) before upgrading or removing the package; if you enabled *Start at login*, delete
`~/.config/autostart/mydock.desktop` after removing it.

The version is `<Version>` in `Directory.Build.props`. Both architectures build from any machine
with `dotnet` and `dpkg-deb` (`ARCHES=amd64 packaging/build-deb.sh` builds only one).

### Releases

Pushing a tag that matches the version publishes a GitHub release with both packages and their checksums:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

The workflow is `.github/workflows/release.yml`; running it by hand from the Actions tab only builds and tests.

## Usage

| Command | Effect |
|---|---|
| `mydock` | start and show the panel; if already running, show it |
| `mydock --hidden` | start with the tray icon only |
| `mydock --toggle` | open or close the panel of the running instance |
| `mydock --quit` | quit the running instance |
| `mydock --dump` | print the containers and resources to the terminal and exit |

On GNOME a click on the icon opens its menu, whose first entry shows or hides the panel.
To have it a keystroke away, bind `mydock --toggle` to a shortcut in
*Settings → Keyboard → Custom Shortcuts*.

In the panel:

- the panel closes by itself when it loses focus; the pin keeps it open. Drag the header to move it;
- *Sort* cycles through name, CPU and RAM. When sorting by name, containers of the same Compose project are grouped;
- stopping and restarting ask for a second click to confirm;
- *Stopped* lists the containers that are not running, which can be started again from there, or deleted with the trash button.
  Deleting asks whether to remove the image too (only that tag is dropped); the option is disabled while another container uses the image;
- the file button in the header loads a `Dockerfile` or `docker-compose.yml` (also `compose.yaml`, `Dockerfile.dev`, ...) into the *Files* section.
  The `.env*` files next to it (`.env`, `.env.dev`, ...) are listed in a dropdown beside the play button, which runs the file with the chosen one:
  `docker compose -f <file> --env-file <env> up -d` for compose, `docker build` then `docker run -d --env-file <env>` for a Dockerfile
  (image `mydock/<folder>`). The loaded files and the chosen `.env` are remembered. This needs the `docker` CLI in the `PATH`.

CPU follows the `docker stats` convention: 100% is one full core. RAM excludes reclaimable page cache.
Statistics are only sampled while the panel is open.

For a daemon other than the local one, use the `DOCKER_HOST` variable. Settings live in `~/.config/mydock/settings.json`.

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/MyDock.App
```

Avalonia collects anonymous usage data at build time (not in the installed app);
to opt out, set `AVALONIA_TELEMETRY_OPTOUT=1` in the environment.

| Project | Contents |
|---|---|
| `src/MyDock.Core` | Docker access (`IDockerService`), Dockerfile/compose runner (`IComposeRunner`), statistics calculation, `ContainerMonitor`. No UI dependencies |
| `src/MyDock.App` | tray icon, panel, view models |
| `tests/MyDock.Core.Tests` | Core tests, with a fake Docker service |

`ContainerMonitor` keeps the list up to date by listening to Docker events, without polling,
and reconnects by itself if the daemon goes away.

The panel is placed at the top right because Avalonia on Linux uses X11 (here through XWayland):
a native Wayland client cannot choose where to appear.

## License

[MIT](LICENSE): use, copy, modify and distribute it freely, keeping the copyright notice.
