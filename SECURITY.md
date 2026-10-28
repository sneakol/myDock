# Security policy

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's private reporting instead:
the **Security** tab of this repository → **Report a vulnerability**.

Include what you found, how to reproduce it and the version (`<Version>` in `Directory.Build.props`, or the package version).
I will answer as soon as I can; this is a small project maintained by one person, so there is no guaranteed response time.

## Supported versions

Only the latest release gets fixes.

## What to keep in mind

myDock talks to the Docker daemon through its socket (`/var/run/docker.sock` or `DOCKER_HOST`).
Access to that socket is effectively root on the machine, and the app can start, stop and delete containers
and images and run `docker` commands (compose, build, run, exec) on your behalf. It makes no network connections of its own
beyond the daemon you point it at.
