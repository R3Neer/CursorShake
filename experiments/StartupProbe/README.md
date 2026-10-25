# StartupProbe — prueba aislada del arranque por usuario

25-oct-2026. Prueba mediante COM del Programador de tareas, no instalador ni implementación de CursorShake. [Resultados canónicos](results.json): artifacts/run-02/inner-result.json, sin identificadores personales. Artefactos locales ignorados incluyen marcadores y XML técnico.

Un lanzador temporal inicia el auxiliar sin elevación en la sesión interactiva. Ese proceso crea su propia tarea temporal con disparador de inicio de sesión del usuario actual, InteractiveToken y LeastPrivilege. Delay=PT0S, ExecutionTimeLimit=PT0S, sin condiciones de red/reposo/batería. Se comprueba lanzamiento con espacios en ejecutable y argumentos, identidad y sesión correctas y proceso hijo no administrador. El lanzamiento manual Task.Run tardó 307 ms hasta el marcador. Ambas tareas se eliminaron y se verificó su ausencia.

No se cerró sesión: ActualLogonLatencyTested=false. Ese tiempo de lanzamiento manual no demuestra disponibilidad antes de un minuto tras un inicio de sesión real. El objetivo de producto acordado es menos de un minuto; requiere validación con el futuro ejecutable instalado. No queda ninguna entrada de inicio automático de CursorShake.

```powershell
dotnet run --project experiments/StartupProbe -- C:\ruta\nueva\resultado
```

La prueba crea tareas CursorShakeProbe-* temporales y las retira en finally. No ejecutar concurrentemente con otra copia. No es un mecanismo de instalación. La política de instancias del producto y sesiones RDP debe concordar con SingleInstanceGuard; el IgnoreNew del auxiliar no resuelve esa decisión.

Fuentes: [LogonTrigger](https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger), [contexto de seguridad](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks), [ExecutionTimeLimit](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-executiontimelimit).
