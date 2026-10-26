# No background process: priorities are applied by Windows, not by GPM

GPM writes each Priority Override into IFEO once and exits; Windows applies it every time the exe starts. The alternative, a 24/7 service that watches processes and re-prioritizes them (Process Lasso's model), was rejected: a passive registry write is the safest approach for games with kernel anti-cheat like EAC. The cost is accepted on purpose: anything IFEO cannot express (core affinity) stays out of scope rather than pulling a background process in. No tray icon either.
