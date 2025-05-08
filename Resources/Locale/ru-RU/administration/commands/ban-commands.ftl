### Ban command loc
## Shown when a ban is applied to a player

cmd-ban-player-announce = { $admin } забанил { $player }.

cmd-ban-boot-count = Забаненных игроков выкинуло: { $count }
cmd-ban-success = Забанены { $target } IPs, HWID, и UserID. Причина: { $reason }
cmd-ban-change-expires = Время окончания бана { $player } было обновлено с { $originalTime } на { $newTime }.

## Alert messages for bans
user-banned-for = Вы были забанены по причине: { $reason }. (Оставшееся время: { $expires })
user-banned-permanent = Вы были забанены по причине: { $reason }. Бан постоянный.
user-saw-ban-info = Бан-лист, БД: { $banId } { $bannedOn }. { $reason } { $expires } 