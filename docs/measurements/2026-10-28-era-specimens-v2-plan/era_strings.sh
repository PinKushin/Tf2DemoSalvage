#!/bin/bash
# Per era: does each name the specimen cfg uses appear as a whole NUL-delimited string in that era's binaries?
# A ConCommand/ConVar name is registered from a string literal, so absence from every module = not in that build.
# Control: names that must be present in every era (record, kill, joinclass) - if those read absent, the instrument is broken.
export LC_ALL=C
names="sv_cheats cl_disablehtmlmotd fps_max mp_respawnwavetime mp_disable_respawn_times mp_waitingforplayers_cancel tf_bot_quota tf_bot_add hidepanel record stop disconnect jointeam joinclass taunt kill explode slot1 slot2 slot3 build destroy disguise addcond removecond wait alias echo tv_enable tv_record tv_stoprecord con_logfile version heavyweapons +attack2 +reload +duck +jump +moveleft cl_savescreenshotstosteam sv_allow_wait_command"
declare -A roots=(
  [2007]="F:/tf2-builds/tf2-2007/Team Fortress 2"
  [2008]="F:/tf2-builds/tf2-2008"
  [2010]="D:/tf2-builds/tf2-2010-1.1.0.0"
  [2011]="F:/tf2-builds/tf2-2011/Team Fortress 2"
  [2013]="F:/tf2-builds/tf2-2013/TF2"
  [live]="F:/SteamLibrary/steamapps/common/Team Fortress 2"
)
roots[2009]="D:/team fortress 2"
for era in ${ERAS:-2007 2008 2010 2011 2013 live}; do
  r="${roots[$era]}"
  mods=()
  for m in "$r/bin/engine.dll" "$r/tf/bin/server.dll" "$r/tf/bin/client.dll" "$r/bin/x64/engine.dll" "$r/tf/bin/x64/server.dll" "$r/tf/bin/x64/client.dll" "$r/bin/tier0.dll" "$r/bin/vstdlib.dll" "$r/bin/x64/vstdlib.dll"; do
    [ -f "$m" ] && mods+=("$m")
  done
  echo "== $era (${#mods[@]} modules: $(for m in "${mods[@]}"; do basename -- "$(dirname -- "$m")"; done | tr '\n' ' '))"
  for n in $names; do
    hits=""
    for m in "${mods[@]}"; do
      if grep -aqP -- "\x00\Q$n\E\x00" "$m"; then hits="$hits $(basename -- "$m")"; fi
    done
    printf '  %-32s %s\n' "$n" "${hits:-ABSENT}"
  done
done
