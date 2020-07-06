# RedworkDE.DVMP
This is a multiplayer mod for Derail Valley

## Features
- Player position sync
  - todo: support parenting to trains
  - todo: unbreak map markers
  - todo: hands / models
- train car sync
  - cargo sync
  - coupling sync
  - todo: damage sync
- loco sync
  - shunter
  - todo: all other locos
  - spawning is disabled of them
- train set sync
  - todo: maybe optimize the thresholds for the allowable error
  - todo: moving all cars to one host
  - **all cars that are coupled together should be from one host (spawned by one player)**
- junction sync
- todo: turntable sync
- todo: jobs
- Hard player limit: 1023 local connections max per session
  - todo: properly clean up after players disconnect
- todo: NAT hole-punching
- todo: matchmaking / 
- todo: probably a lot more still

## Semi-related changes
- jobs and locos do not spawn
- stops the game from forcing you into full screen
- stops the game from pausing when in the menu or tabbed out
- forcefully enabled spawn mode on the remote and enables it to spawn locos
- add a instance number to the end of the title for recording multiple instance with OBS

## Installation: 
- ~~Install BepInEx (https://github.com/BepInEx/BepInEx/releases) version 5.1 or later~~ Currently included
- Extract into `steamapps\Derail Valley` next to `DerailValley.exe`

## Testing on one Computer
- start the game twice in windowed mode
- on one instance run the console commands `mp.authority` and `mp.listen`
- on the other run `mp.connect`
- **all cars that are coupled together should be from one host (spawned by one player)**

## Networked testing
- all but one client must be able to accept connections from the other client
  - in practice this mean you must configure port forwarding in you router
  - the default port is tcp:2000 but any TCP port can be used
- one player must use the console commands `mp.authority` and `mp.listen [<port>]`
- the other player must run `mp.connect <public IP of the first player> [<port>]`
- if the port arguments are omitted, they will default to 2000
- both IPv4 and IPv6 addresses are supported, DNS names will not be resolved
- Player names will be taken from Steam, if possible
  - the local player name can be set with the console command `mp.username <new username>` (spaces are not supported)
  - non-ASCII characters are theoretically supported, but probably not available in the font
- the player color is chosen at (very pseudo) random
  - can be set to any RBG color with `mp.playercolor <R> <G> <B>`
- **all cars that are coupled together should be from one host (spawned by one player)**

## Connecting more than 2 players
- todo: currently bugged, doesn’t work
- every client must connection to each other
- first connection  must be to the authority, other than that order shouldn’t matter as long as at the end before players actually start playing everyone is connected to everyone else

## Building
- Derail Valley is required for building
- It must be located in or symlinked from `C:\DerailValley`
- Run `mklink /J C:\DerailValley "<Path to DV>"` in a admininstrator level command prompt to create the symlink
- Alternativly provide the game path during build as a command line parameter: `dotnet build -p:GameDirectory=<Path to DV>`
