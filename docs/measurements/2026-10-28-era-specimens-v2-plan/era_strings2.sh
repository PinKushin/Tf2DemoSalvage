#!/bin/bash
# Second pass: substring (no NUL boundary) and case-insensitive, to rule out tail-merged/differently-cased strings
# behind the ABSENT verdicts of era_strings.sh; plus any TF_COND_* name strings each era ships.
export LC_ALL=C
declare -A roots=(
  [2007]="F:/tf2-builds/tf2-2007/Team Fortress 2"
  [2008]="F:/tf2-builds/tf2-2008"
  [2011]="F:/tf2-builds/tf2-2011/Team Fortress 2"
  [2013]="F:/tf2-builds/tf2-2013/TF2"
  [live]="F:/SteamLibrary/steamapps/common/Team Fortress 2"
)
roots[2009]="D:/team fortress 2"
for era in ${ERAS:-2007 2008 2011 2013 live}; do
  r="${roots[$era]}"
  echo "== $era"
  for m in "$r/tf/bin/server.dll" "$r/tf/bin/client.dll"; do
    b=$(basename -- "$m")
    for n in heavyweapons addcond tf_bot_add cl_disablehtmlmotd mp_disable_respawn_times; do
      c=$(grep -aoi -- "$n" "$m" | sort | uniq -c | tr '\n' ' ')
      printf '  %-10s %-26s %s\n' "$b" "$n" "${c:-none}"
    done
    printf '  %-10s TF_COND_ names: %s\n' "$b" "$(grep -aoP -- 'TF_COND_[A-Z0-9_]+' "$m" | sort -u | wc -l)"
  done
done
