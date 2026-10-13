# LAN-first experimental text transport

This is a fork of [Jeric-X/SyncClipboard](https://github.com/Jeric-X/SyncClipboard), originally authored by JericX, under MIT. Original history, copyright and third-party notices are retained. New code also uses MIT.

The opt-in Windows LAN endpoint uses the existing OS clipboard factory, setter and synchronization locks. It advertises a persistent UUID-based `.local` hostname and `_syncclipboard._tcp` service using native Windows DNS-SD. It requires a random 256-bit Bearer token and only listens on Private IPv4 networks by default. Public/domain access requires explicit opt-in. No credentials or clipboard data are advertised.

The NAS extension retains existing endpoints and adds authenticated `/api/lan-first/v1` routes and SignalR request correlation. Enabling text on-demand mode stops automatic text uploads and ignores stale legacy text snapshots. Remote requests ask the online Windows client for current content. NAS does not persist these responses. Offline clients return an explicit failure; there is no offline send queue yet. Images/files and independently enabled history synchronization continue through their original channels.

Writes require the hash from a preceding read. Conflicting writes fail instead of using device wall clocks. A bounded Windows request ledger prevents duplicate writes across LAN and NAS. Native OS copies can still race between the final read and write; atomic Win32 conditional updates are not implemented.

This is **not a production-ready implementation of the entire requested architecture**. New APIs support text only. iPhone Shortcuts instructions are provided in [setup](lan-first-setup.md), but no signed or device-tested shortcut has been delivered. The supported fallback is an explicit menu selecting LAN or NAS actions. Automatic network-error recovery is not claimed. Native registration succeeded locally, while local hostname resolution timed out; iPhone resolution remains unverified. See the [validation matrix](lan-first-validation.md) for actual build/test outcomes and open requirements.

Keep the working official installation, NAS image and shortcuts. Upgrade the NAS extension before enabling on-demand mode; verify `/api/lan-first/v1/capabilities`. To roll back, disable on-demand mode first, then restore the previous NAS image and backed-up persistent data. Public NAS access must remain HTTPS. Do not expose the LAN HTTP port to the Internet.
