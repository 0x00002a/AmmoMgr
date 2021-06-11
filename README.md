# Ammo Manager 

## Introduction 

Weapons kinda suck at sharing ammo, in the event that one of them actually draws ammo from any containers you just know its gonna hog it all rather than sharing. This script 
does a robin hood - yoinks its excess its ammo and shares it between all other weapons on the same network. Sound cool? That's not all my friend, it also displays 
all your weapons ammo levels with pretty progress bars and colours!

## Features 

- Equalizes ammo between all connected weapons. e.g. if there are 50 NATO cases and 2 connected turrets, each will get 25 cases
- Works on a per network basis, if there are 50 NATO cases and 5 turrets but only 2 turrets are connected to each other, one of which has all the ammo, both of the connected turrets get 25 and the others get 0
- Works on a per ammo basis. Meaning if there are 2 missile turrets and 2 gatling turrets on the same network, with 20 missiles, the missile turrets each get 10 (rather than 5)
- Diverts ammo from inactive weapons toward currently engaged weapons. Even if you're running a little low on ammo, your hot weapons will keep shooting until the very end
- Works with WeaponCore or Vanilla turrets (although mostly tested with WeaponCore)
- Lots of pretty (imo) display options with ammo and fill level stats for containers, weapons, or both, as well as single-line versions for quick combat checks 

### Configuration 

If you just want the ammo balancing part, no configuration is needed, just slap it in a PB and forget about it.

This scripts config for displaying on LCDs is a bit involved, see the top of the script for detailed instructions.

### Docking via connectors

This script watches only inventories on the same grid. This means it will not cross connector boundaries but it will still manage custom turrets (i.e. connections through rotors or hinges). Currently 
connectors are the only supported way to dock if you wanna keep the script running on both grids. 

### Multiple instances on the same grid (or docking via merge-blocks)

This script will probably mess up and implode or something if you have multiple PBs running it on the same grid. I'm not gonna fix that one, if you have multiple instances of the script on the same 
grid you are doing something very wrong... or using merge blocks for docking. This script is NOT currently compatible with merge blocks. I personally don't use them for docking and I lack the 
experience to implement compatibility without impacting performance. If for some god forsaken reason you _do_ use them for docking, just turn one of the scripts off beforehand.

## License 

This script is GPLv3. That means you can reupload it or any script that contains it _as long as_ you:

- Keep all existing license notices intact
- Credit me
- List your changes (easiest way is with git and github repo)
- Make _all_ the source code of the relevant script available freely and publicly with no restrictions placed on its access.
- Make your script GPLv3 as well
- Give me your first born child

(ok that last one isn't actually legally binding)

If in doubt, ask me in comments or the Keen discord (\@Natomic). 
Full license is available [here](https://github.com/0x00002a/AmmoMgr/blob/220c418739ff811b354517f661e4f7aa7f3cf9b8/LICENSE). I reserve the right to ask 
for your script to be yeeted if you have reused my script without obeying the license.

### Source

The full source code for this script can be found here: https://github.com/0x00002a/AmmoMgr

### MDK 

This script is developed and deployed using the [MDK](https://github.com/malware-dev/MDK-SE). If you wanna get into SE scripting, check it out. Also check out the Keen Discord, lots of 
helpful people on there.

### Stuff used in screenshots 

- [Whips TBR](https://steamcommunity.com/sharedfiles/filedetails/?id=1707280190)
- [Whips Artificial Horizon](https://steamcommunity.com/sharedfiles/filedetails/?id=1721247350)
- [Automatic LCDs 2](https://steamcommunity.com/sharedfiles/filedetails/?id=822950976)
- [Adjustable LCDs](https://steamcommunity.com/sharedfiles/filedetails/?id=2427400629) (mine)
