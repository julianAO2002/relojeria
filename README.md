# Relojería

Sincronización de relojes en sistemas distribuidos con **C#, ASP.NET Core y Docker**. Los tres algoritmos del caso práctico funcionando como servicios independientes:

- **Cristian**: un servidor de tiempo y tres clientes que estiman la hora con `hora del servidor + RTT/2`.
- **Berkeley**: un coordinador que promedia los relojes de cuatro servidores (él incluido) y le manda a cada uno su ajuste.
- **Lamport**: `OrdersService`, `InventoryService` y `PaymentsService` intercambian mensajes HTTP con su reloj lógico en un header.

Caso práctico de sincronización — Sistemas Operativos / Sistemas Distribuidos (FCyT – UADER). Autor: Julián Olivera.

El informe en PDF con todas las respuestas y los registros de ejecución está en [`docs/Caso_Sincronizacion_Olivera.pdf`](docs/Caso_Sincronizacion_Olivera.pdf).

## Contenido

| Ruta | Qué hay |
|---|---|
| `src/Clocks/` | Biblioteca compartida: reloj simulado, lógica pura de Cristian y Berkeley, relojes de Lamport y vectoriales, registro de eventos. |
| `src/Cristian.TimeServer/` | Web API: `GET /api/time` (con latencia de red simulable). |
| `src/Cristian.Client/` | Cliente: mide T0, T1 y RTT, estima la hora, ajusta su reloj de aplicación. |
| `src/Berkeley.Node/` | Web API de cada nodo: `GET /api/clock`, `POST /api/clock/adjust`. |
| `src/Berkeley.Coordinator/` | Coordinador: sondea, promedia y envía los ajustes cada `INTERVAL_SECONDS`. |
| `src/Lamport.Orders/`, `src/Lamport.Inventory/`, `src/Lamport.Payments/` | Los tres servicios del ejercicio 3; `GET /api/timeline` en Orders junta los eventos de los tres. |
| `tests/Clocks.Tests/` | 10 tests unitarios (xUnit) de la lógica de los algoritmos. |
| `Dockerfile` | Un único Dockerfile multi-etapa; el proyecto se elige con `--build-arg PROJECT=<nombre>`. |
| `docker-compose.yml` | Un *profile* por ejercicio: `cristian`, `berkeley`, `lamport`. |
| `scripts/` | Un script por ejercicio que levanta el escenario, provoca las fallas y guarda el registro en `docs/salidas/`. |
| `docs/salidas/` | Registros de ejecución reales. |

## Cómo se simulan los relojes

Todos los contenedores de Docker comparten el reloj del kernel del host, así que no se puede desfasar el reloj del sistema operativo de cada uno (y el enunciado pide justamente **no** tocarlo). Cada aplicación tiene un **reloj de aplicación** (`SimulatedClock`):

```text
hora = hora real UTC + desfase inicial + deriva + correcciones aplicadas por el algoritmo
```

| Variable | Significado |
|---|---|
| `CLOCK_OFFSET_SECONDS` | Desfase inicial (por ejemplo `8` para el servidor B del enunciado). |
| `CLOCK_DRIFT_PPM` | Deriva en partes por millón: `1000` = el reloj adelanta 1 ms por segundo. |
| `SCENARIO_START_UTC` | Instante real que se muestra como `10:00:00` (lo fijan los scripts), para que los registros coincidan con las horas del enunciado. Vacío = hora UTC real. |

Los algoritmos solo usan `Adjust(...)` sobre ese reloj. Como la hora real es común a todos los contenedores, los registros pueden mostrar el **error real** de cada estimación, algo que en un sistema real no se puede medir. Ese valor es solo para el análisis: el algoritmo nunca lo lee.

## Ejecución

Requisitos: Docker con Compose v2 y Bash (Git Bash en Windows). Para los tests, .NET SDK 8 o superior.

```bash
dotnet test tests/Clocks.Tests              # 10 tests de la logica

bash scripts/ej1_cristian.sh                 # -> docs/salidas/ej1_cristian.txt
bash scripts/ej2_berkeley.sh                 # -> docs/salidas/ej2_berkeley.txt (7 escenarios, ~3 min)
bash scripts/ej3_lamport.sh                  # -> docs/salidas/ej3_lamport.txt

# O a mano, por ejercicio:
docker compose --profile cristian up --build
docker compose --profile berkeley up --build
docker compose --profile lamport up --build -d
curl -X POST localhost:8090/api/orders -H "content-type: application/json" -d '{"productId":"P001","quantity":2,"amount":150}'
curl localhost:8090/api/timeline
```

---

# Respuestas

## Preguntas teóricas

**1. ¿Cuál es la diferencia entre un reloj físico y un reloj lógico?**
Un **reloj físico** mide el paso del tiempo real: en una computadora es un oscilador de cuarzo que genera interrupciones a frecuencia fija, que el sistema operativo cuenta para obtener la hora. Sincronizarlo significa acercar su valor a una referencia (UTC) o a los otros relojes. Un **reloj lógico** no mide tiempo: es un contador que numera los eventos de modo que respeten la causalidad (si *a* causó *b*, el número de *a* es menor). No dice qué hora es ni cuánto tiempo pasó, solo qué ocurrió antes de qué.

| | Reloj físico | Reloj lógico |
|---|---|---|
| Representa | La hora real (aproximada) | El orden de los eventos |
| Base | Hardware (cuarzo) | Software (contador) |
| Se sincroniza con | UTC, NTP, Cristian, Berkeley | Las marcas de tiempo de los mensajes |
| Problema principal | La deriva | No mide duraciones; solo captura causalidad |
| Uso | Auditoría, certificados, timeouts, logs con fecha | Orden de mensajes, exclusión mutua distribuida, depuración |

**2. ¿Qué es la deriva de un reloj y cómo afecta a los sistemas distribuidos?**
La **deriva** (*drift*) es la diferencia de **velocidad** entre un reloj y el tiempo real (o entre dos relojes). Ningún cristal oscila exactamente a su frecuencia nominal: depende de la fabricación, la temperatura y el envejecimiento. Si *C(t)* es el valor del reloj en el instante real *t*, un reloj perfecto cumple *dC/dt = 1*, y uno real cumple *1 − ρ ≤ dC/dt ≤ 1 + ρ*, donde ρ es la tasa máxima de deriva (en cuarzo típico ≈ 10⁻⁶, unos 31 s por año). Hay que distinguirla del **desfase** (*offset* o *skew*), que es la diferencia de *valor* en un instante: la deriva es lo que hace crecer el desfase.

En un sistema distribuido, aunque todos los relojes se pongan en hora, con el tiempo se separan: dos relojes que derivan en sentidos opuestos difieren hasta 2ρΔt después de Δt. Eso rompe todo lo que compara marcas de tiempo de distintas máquinas: logs que no se pueden ordenar, un archivo que parece más viejo que su fuente (`make` no recompila), certificados o tokens “todavía no válidos” o “vencidos”, timeouts y alquileres (*leases*) mal calculados, o el criterio “gana la última escritura” en bases replicadas que pisa el dato equivocado. Por eso la sincronización tiene que repetirse periódicamente, al menos cada δ/2ρ segundos para no superar un desfase δ. En la práctica se ve en el ejercicio 1: el servidor B adelanta 5000 ppm y hay que corregirlo 20 ms cada 4 s; con el servidor de tiempo caído pasó unos 10 s sin sincronizar y su error llegó a +53 ms.

**3. ¿Qué es el Tiempo Atómico Internacional (TAI) y cuál es su relación con UTC?**
El **TAI** es la escala de tiempo calculada por la Oficina Internacional de Pesas y Medidas (BIPM, París) promediando cientos de relojes atómicos de cesio 133 de laboratorios de todo el mundo. Desde 1967 el segundo se define como 9.192.631.770 períodos de la radiación de transición del cesio 133. Es extremadamente estable, pero no sigue la rotación de la Tierra, que se va frenando: el día solar se alarga y el TAI se adelanta respecto del tiempo astronómico. El **UTC** (Tiempo Universal Coordinado) es el TAI corregido con **segundos intercalares** (*leap seconds*) que se agregan cuando la diferencia con el tiempo solar se acerca a 0,9 s. Por eso UTC avanza al ritmo del TAI pero va “atrasado” un número entero de segundos (37 s desde 2017). UTC es la base de la hora civil y se distribuye por radio, GPS e Internet (NTP).

**4. ¿Por qué resulta difícil establecer un orden global de eventos utilizando únicamente relojes físicos?**
Porque no existe un reloj global: cada máquina tiene el suyo, con desfase y deriva distintos, y aun sincronizados quedan diferencias del orden de la precisión de la sincronización (milisegundos en una LAN con NTP, más en Internet). Si dos eventos de máquinas distintas ocurren más cerca que esa precisión, sus marcas de tiempo pueden quedar invertidas. Peor aún: un mensaje puede aparecer **recibido antes de ser enviado**. En el ejercicio 3, con el reloj de `InventoryService` 3 s atrasado, el registro dice que Inventory recibió el pedido **2,861 s antes** de que Orders lo enviara, y que Orders recibió la confirmación de pago **4,990 s antes** de que Payments la mandara. Ordenar por hora física da 93 pares de eventos en orden imposible. Además, la latencia de red es variable y desconocida, así que ni siquiera se puede saber con exactitud cuándo se leyó un reloj remoto.

**5. ¿Cómo funciona el algoritmo de Cristian? Explique sus ventajas y limitaciones.**
Hay un **servidor de tiempo** (con una buena referencia, por ejemplo un receptor de UTC). Cada cliente:
1. anota su hora local **T0** y le pide la hora al servidor;
2. el servidor responde con su hora **Ts**;
3. el cliente anota **T1** al recibir la respuesta y calcula **RTT = T1 − T0**;
4. estima la hora actual como **Ts + RTT/2**, suponiendo que la ida y la vuelta tardaron lo mismo;
5. ajusta su reloj. La precisión de la estimación es **±RTT/2**, porque el servidor leyó su reloj en algún momento dentro de ese intervalo.

Para mejorar el resultado se toman varias muestras y se usa la de menor RTT (la más precisa) o se descartan las de RTT excesivo, que es lo que hace el cliente implementado. El tiempo de procesamiento del servidor, si se conoce, puede restarse del RTT. Además, el reloj no debería ir hacia atrás: si hay que atrasarlo, conviene frenarlo de a poco (*slewing*).

- **Ventajas**: muy simple, pocos mensajes (dos por sincronización), buena precisión en redes con RTT chico y simétrico (en Docker, error real de 0,1 ms), y sincroniza con una referencia externa (hora real).
- **Limitaciones**: el servidor es un **punto único de falla** y un posible cuello de botella; supone latencia **simétrica** (con 50 ms de ida y 450 ms de vuelta el error fue de −201 ms); con RTT grande la precisión empeora; si el servidor da una hora errónea o es malicioso, todos los clientes la copian; y no tolera fallas bizantinas.

**6. ¿Cómo funciona el algoritmo de Berkeley? ¿Qué función cumple el nodo coordinador?**
No supone que alguien tenga la hora correcta: busca que **todos coincidan entre sí**. El **coordinador** (*time daemon*):
1. le pide la hora a cada nodo (compensando la latencia con RTT/2, como Cristian);
2. calcula la diferencia de cada uno con su propio reloj;
3. promedia las diferencias **incluyendo la suya** (0), opcionalmente descartando los relojes muy desviados (promedio tolerante a fallas);
4. calcula para cada nodo el ajuste = promedio − diferencia y
5. le envía a cada uno **el ajuste**, no la hora: un valor relativo no se desactualiza por lo que tarde en llegar el mensaje.
6. Repite periódicamente.

El coordinador **organiza** la sincronización: sondea, calcula el promedio y distribuye las correcciones. No es una fuente de hora “verdadera”: él también se ajusta al promedio. Se lo elige con algún algoritmo de elección (por ejemplo el del matón, *bully*) y, si falla, se elige otro.

**7. ¿Qué diferencias existen entre los algoritmos de Cristian y Berkeley?**

| | Cristian | Berkeley |
|---|---|---|
| Modelo | Cliente–servidor: el cliente *pide* (pull) | Coordinador activo: *sondea* a los nodos (push de ajustes) |
| Referencia | El servidor tiene la hora correcta (por ejemplo UTC) | Nadie tiene la hora correcta; se busca el acuerdo |
| Resultado | Todos copian la hora del servidor (sincronización externa) | Todos convergen al promedio (sincronización interna); puede alejarse de UTC |
| ¿Quién ajusta? | Solo los clientes | Todos, incluido el coordinador |
| Qué se envía | La hora del servidor | El ajuste relativo de cada nodo |
| Relojes desviados | Si el servidor está mal, todos quedan mal | El promedio tolerante a fallas descarta los relojes atípicos |
| Uso típico | Clientes con acceso a un servidor UTC (NTP se basa en esta idea) | Redes aisladas sin acceso a UTC (el original era para Berkeley UNIX) |

**8. ¿Qué es la relación happens-before definida por Lamport?**
Es la relación de orden “ocurre antes” (**a → b**) que se puede determinar **sin relojes**, solo observando el sistema:
1. si *a* y *b* son eventos del **mismo proceso** y *a* ocurre antes que *b*, entonces *a → b*;
2. si *a* es el **envío** de un mensaje y *b* su **recepción**, entonces *a → b* (no se puede recibir algo antes de enviarlo);
3. es **transitiva**: si *a → b* y *b → c*, entonces *a → c*.

Captura la **causalidad potencial**: si *a → b*, *a* pudo haber influido en *b*. Es un orden **parcial**: hay pares de eventos que no están relacionados. Lamport parte de la observación de que, en la mayoría de los casos, los procesos no necesitan saber qué hora es, sino ponerse de acuerdo en el orden de los eventos que se afectan entre sí.

**9. ¿Cómo funcionan los relojes lógicos de Lamport?**
Cada proceso *Pi* tiene un contador *Ci* que empieza en 0:
1. **antes de cada evento** (interno o de envío): *Ci = Ci + 1*;
2. **al enviar** un mensaje, le adjunta el valor *Ci* (aquí, en el header HTTP `X-Lamport-Clock`);
3. **al recibir** un mensaje con marca *t*: *Ci = max(Ci, t) + 1*, y ese valor es el del evento de recepción.

Así se cumple la **condición de reloj**: si *a → b* entonces *C(a) < C(b)*. Para obtener un **orden total** (necesario por ejemplo en exclusión mutua distribuida o en multicast totalmente ordenado) se desempatan los valores iguales con el identificador del proceso: *(C(a), i) < (C(b), j)*. En el ejercicio 3: Orders envía con L=2, Inventory (que estaba en 0) recibe y pasa a 3, y Payments (que ya estaba en 4 por pagos anteriores) recibe L=7 y pasa a 8.

**10. ¿Qué significa que dos eventos sean concurrentes?**
Que **ninguno ocurre antes que el otro**: no se cumple *a → b* ni *b → a* (se escribe *a ‖ b*). No hay ninguna cadena de eventos locales y mensajes que los conecte, así que ninguno pudo influir en el otro. No significa que ocurrieran “al mismo tiempo” en el reloj de pared, sino que es **imposible y además irrelevante** decidir cuál fue primero. En el ejercicio 3, “Pedido recibido ORD-001” en Orders y los pagos de otros pedidos que procesó Payments son concurrentes: Payments no supo nada de ese pedido hasta que recibió el mensaje L=7.

**11. ¿Qué diferencias existen entre sincronizar relojes físicos y establecer un orden lógico de eventos?**

| | Sincronización de relojes físicos | Orden lógico de eventos |
|---|---|---|
| Objetivo | Que los relojes marquen (casi) la misma hora, y opcionalmente la hora real | Que todos coincidan en el orden causal de los eventos |
| Mecanismos | Cristian, Berkeley, NTP, PTP | Relojes de Lamport, relojes vectoriales |
| Precisión | Aproximada: siempre queda un error (±RTT/2, deriva) | Exacta respecto de la causalidad |
| Costo | Mensajes periódicos de sincronización | Unos bytes extra en cada mensaje de la aplicación |
| Qué responde | “¿Qué hora es?” y “¿cuánto duró?” | “¿Qué pasó antes?” |
| Limitación | No garantiza el orden de eventos más cercanos que la precisión | No dice nada de la hora real ni de las duraciones |

Se complementan: el ejercicio 3 muestra que un error de unos pocos segundos en el reloj físico invierte el orden de los mensajes, mientras que Lamport los ordena bien sin importar ese error.

**12. ¿Cómo funciona NTP y qué problemas permite resolver?**
**NTP** (*Network Time Protocol*, RFC 5905) sincroniza relojes con UTC a través de Internet. Usa una jerarquía de servidores por **estratos**: el estrato 0 son las fuentes de referencia (relojes atómicos, GPS), el estrato 1 los servidores conectados directamente a ellas, el 2 los que se sincronizan con el 1, etc. Cada intercambio registra cuatro marcas de tiempo: *T1* (envío del cliente), *T2* (recepción en el servidor), *T3* (envío del servidor) y *T4* (recepción en el cliente), y con ellas calcula:

```text
desfase  θ = ((T2 − T1) + (T3 − T4)) / 2
demora   δ = (T4 − T1) − (T3 − T2)
```

Es la idea de Cristian, pero descontando el tiempo de proceso del servidor. NTP repite el intercambio, se queda con las muestras de menor demora, filtra y combina varios servidores para descartar los erróneos, y corrige el reloj de a poco (*slewing*) en vez de saltar, para que nunca vaya hacia atrás. Logra unos pocos milisegundos de precisión en Internet y menos de uno en una LAN.

Resuelve: la **deriva** de los relojes de cuarzo (corrección continua), la **latencia variable** de la red (estimación de demora y filtrado), la **escalabilidad** (jerarquía de estratos y redundancia) y la **tolerancia a fallas** (varios servidores, descarte de los que no coinciden, autenticación opcional). Es lo que mantiene en hora prácticamente todas las computadoras y servidores conectados.

---

## Ejercicio 1 — Algoritmo de Cristian

**Implementación.** `Cristian.TimeServer` es una Web API con `GET /api/time` que devuelve la hora de su reloj (10:00:02 al inicio). Para simular la red acepta `requestDelayMs` (espera antes de leer el reloj, el viaje de ida) y `responseDelayMs` (espera después, el viaje de vuelta). Tres contenedores del cliente (`Cristian.Client`) representan a los servidores A (10:00:00), B (10:00:08, con una deriva de 5000 ppm) y C (09:59:55). Cada sincronización hace los siete pasos del enunciado:

```csharp
var t0 = clock.Now;                                   // 2. instante de envio
var stopwatch = Stopwatch.StartNew();
var reply = await http.GetFromJsonAsync<TimeReply>("/api/time?...");   // 1. consulta REST
stopwatch.Stop();
var t1 = clock.Now;                                   // 3. instante de recepcion
var sample = new CristianSample(t0, t1, reply.ServerTime, stopwatch.Elapsed);   // 4. RTT
// 5. EstimatedNow = ServerTime + Rtt / 2 ; Offset = EstimatedNow - LocalReceive
var best = Cristian.BestSample(samples, maxRtt);      // la de menor RTT, descartando RTT > 1000 ms
clock.Adjust(best.Offset);                            // 6. ajusta el reloj de aplicacion
Log($"antes {before} | despues {after} | ajuste {best.Offset} | error real ...");   // 7.
```

El RTT se mide con `Stopwatch` (un reloj monotónico), no restando horas del reloj que se está ajustando.

**Resultados** (`docs/salidas/ej1_cristian.txt`):

```text
Servidor A (10:00:00) - fase 'normal'
  muestra 1: T0=10:00:00.040 T1=10:00:00.125 servidor=10:00:02.055 RTT=85.3 ms
  muestra 2: T0=10:00:00.126 T1=10:00:00.128 servidor=10:00:02.127 RTT=1.8 ms
  muestra 3: T0=10:00:00.128 T1=10:00:00.129 servidor=10:00:02.128 RTT=1.0 ms
  mejor muestra RTT=1.0 ms -> hora estimada = servidor + RTT/2 (precision +/-0.5 ms)
  antes 10:00:00.135 | despues 10:00:02.135 | ajuste +2.000 s | error real -0.1 ms

Servidor B (10:00:08):  antes 10:00:08.215 | despues 10:00:02.214 | ajuste -6.001 s | error real -0.1 ms
Servidor C (09:59:55):  antes 09:59:55.196 | despues 10:00:02.196 | ajuste +7.000 s | error real -0.2 ms
```

| Servidor | Hora inicial | Ajuste | Error real después |
|---|---|---|---|
| A | 10:00:00 | +2,000 s | −0,1 ms |
| B | 10:00:08 | −6,001 s | −0,1 ms |
| C | 09:59:55 | +7,000 s | −0,2 ms |

El servidor A además repite la sincronización en distintas condiciones de red:

| Fase de A | RTT de la mejor muestra | Precisión (±RTT/2) | Error real |
|---|---|---|---|
| Red normal | 1,0 ms | ±0,5 ms | −0,1 ms |
| Latencia simétrica (200 + 200 ms) | 401,7 ms | ±200,9 ms | −0,2 ms |
| Latencia asimétrica (50 + 450 ms) | 504,2 ms | ±252,1 ms | **−201,3 ms** |
| RTT muy alto (1500 + 1500 ms) | 3001,8 ms → las 3 muestras se descartan | — | se mantiene el reloj (−201,3 ms) |
| Resincronización siguiente (red normal) | 0,6 ms | ±0,3 ms | −0,0 ms |

Con deriva, el servidor B se va 20 ms cada 4 s y cada resincronización lo corrige (`ajuste -0.020 s`).

**Falla del servidor de tiempo.** El script detuvo `time-server` durante unos 9 s:

```text
[servidor-B] === Resincronizacion periodica 5/7 ===
  muestra 1: el servidor de tiempo NO responde (TaskCanceledException: ... Timeout of 4 seconds elapsing.)
  muestra 2: el servidor de tiempo NO responde (HttpRequestException: Name or service not known (time-server:8080))
  muestra 3: el servidor de tiempo NO responde (HttpRequestException: Connection refused (time-server:8080))
  sin muestras validas: se mantiene el reloj (correccion acumulada -6.081 s). Error real actual: +53.0 ms
[servidor-B] === Resincronizacion periodica 6/7 ===
  antes 10:00:32.910 | despues 10:00:32.837 | ajuste -0.073 s | error real -0.1 ms

[servidor-C] muestra 2: ... RTT=3998.0 ms  -> DESCARTADA (RTT mayor al limite)
[servidor-A] muestra 3: ... RTT=129.9 ms -> ... ajuste +0.042 s | error real +42.1 ms   (primera respuesta tras reiniciar)
```

**¿Cómo influye la latencia de red en la precisión de la sincronización?**
El cliente no sabe en qué momento del RTT el servidor leyó su reloj; supone que fue justo en la mitad. Por eso el error máximo es **±RTT/2**: a mayor latencia, peor precisión. Con latencia **simétrica** la suposición se cumple y el error sigue siendo casi cero aunque el RTT sea de 400 ms (−0,2 ms). Con latencia **asimétrica** la suposición falla y el error es (ida − vuelta)/2: con 50 ms de ida y 450 ms de vuelta dio exactamente **−201,3 ms**, dentro de la cota de ±252 ms. La variabilidad (*jitter*) también afecta: por eso se toman varias muestras y se usa la de menor RTT. La primera muestra de cada cliente tardó 33–85 ms (conexión TCP nueva, JIT) y quedó descartada frente a la de 1 ms.

**¿Qué sucede si el tiempo de transmisión de ida y vuelta es muy elevado?**
La cota de error ±RTT/2 se vuelve inaceptable (con RTT = 3 s, ±1,5 s) y además la latencia suele ser más irregular, así que la muestra no sirve. El cliente implementado descarta las muestras con RTT mayor a `MAX_RTT_MS` (1000 ms): en la fase “alta” las tres muestras (≈3002 ms) se descartaron y el reloj **no se tocó**, conservando la última corrección válida. Es mejor no ajustar que ajustar con una estimación mala. También pasó en la recuperación del servidor C, cuya muestra de 3998 ms se descartó. Si el RTT alto fuera permanente habría que buscar un servidor más cercano, ampliar el límite aceptando menos precisión, o usar NTP, que filtra y combina muchas muestras.

**¿Qué ocurre si el servidor de tiempo deja de responder?**
Los clientes no pueden sincronizar: las peticiones fallan por *timeout* (4 s) o por conexión rechazada o nombre no resuelto, como muestra el registro. El cliente implementado **sigue funcionando con su última corrección**, pero su reloj **vuelve a derivar** sin control: el servidor B pasó de un error de −0,0 ms a **+53 ms** en los ~10 s que estuvo sin sincronizar (5 ms/s × 10,6 s). Cuando el servidor vuelve, la siguiente resincronización corrige el error acumulado (`ajuste -0.073 s`, error −0,1 ms). Es el punto débil del algoritmo: un único servidor es punto único de falla. Se mitiga con varios servidores de tiempo (consultar a varios y quedarse con la mediana, como hace NTP), reintentos con *backoff* y alertas cuando la última sincronización exitosa es demasiado vieja.

## Ejercicio 2 — Algoritmo de Berkeley

**Implementación.** Cuatro aplicaciones en contenedores: `coordinador` (`Berkeley.Coordinator`) y `nodo-1`, `nodo-2`, `nodo-3` (`Berkeley.Node`, una Web API con `GET /api/clock` y `POST /api/clock/adjust`). En cada ronda el coordinador:

1. sondea a todos los nodos en paralelo (`GET /api/clock`, con timeout de 2 s);
2. calcula la diferencia de cada uno con su propio reloj, compensando la latencia: `hora del nodo + RTT/2 - hora del coordinador`;
3. promedia las diferencias **incluyendo la suya (0)** (`Berkeley.Compute` en `src/Clocks/Berkeley.cs`);
4. calcula el ajuste de cada uno: `promedio - diferencia`;
5. envía a cada nodo **su ajuste** (`POST /api/clock/adjust`) y aplica el propio;
6. repite cada `INTERVAL_SECONDS` (5 s por defecto).

Con `OUTLIER_THRESHOLD_SECONDS > 0` usa el **promedio tolerante a fallas** del algoritmo original: los relojes que difieren de la mediana más que el umbral no entran en el promedio, pero igual reciben su corrección. Un nodo que no responde queda fuera de la ronda. `nodo-2` deriva 2000 ppm (2 ms por segundo) para que se vea la necesidad de resincronizar.

**Escenarios 1 a 3: diferencias iniciales de 5, 10 y 20 segundos** (`docs/salidas/ej2_berkeley.txt`):

```text
Escenario 3 - Relojes iniciales: coordinador 0 s, nodo-1 20 s, nodo-2 -12 s, nodo-3 8 s
===== Ronda 1 =====
participante  hora           diferencia   en promedio  ajuste       hora ajustada
coordinador   10:00:00.828   +0.000 s     si           +4.031 s     10:00:04.859
nodo-1        10:00:20.873   +20.045 s    si           -16.013 s    10:00:04.859
nodo-2        09:59:48.872   -11.956 s    si           +15.987 s    10:00:04.859
nodo-3        10:00:08.864   +8.036 s     si           -4.005 s     10:00:04.859
promedio de diferencias: +4.031 s | dispersion antes del ajuste (max - min): +32.000 s
===== Ronda 2 =====
coordinador   10:00:09.920   +0.000 s     si           -0.028 s     10:00:09.892
nodo-1        10:00:09.876   -0.045 s     si           +0.016 s     10:00:09.892
...
promedio de diferencias: -0.028 s | dispersion antes del ajuste (max - min): +0.045 s
```

| Escenario | Desfases iniciales (nodo-1, nodo-2, nodo-3) | Promedio | Dispersión antes | Dispersión en la ronda 2 |
|---|---|---|---|---|
| 5 s | +5, −3, +2 | +1,024 s | 8,008 s | 0,038 s |
| 10 s | +10, −6, +4 | +2,029 s | 16,010 s | 0,047 s |
| 20 s | +20, −12, +8 | +4,031 s | 32,000 s | 0,045 s |

En todos los casos una ronda alcanza para llevar la dispersión de segundos a **menos de 50 ms**, y la segunda la corrige otra vez. La diferencia residual viene del error de la primera estimación: la primera petición a cada nodo tarda unos 200 ms (conexión nueva, JIT), así que la compensación RTT/2 tiene un error de decenas de ms. El resultado no depende del tamaño de la diferencia inicial: con 20 s queda igual de bien que con 5 s. Notar que **nadie va a la hora del coordinador**: todos van al promedio, que está +1, +2 y +4 s por delante de él. Berkeley logra acuerdo, no exactitud.

**Escenarios 4 y 5: un nodo con 5 minutos de diferencia**

```text
Escenario 4 - promedio simple
coordinador   10:00:00.866   +0.000 s     si           +75.538 s    10:01:16.404
nodo-1        10:00:05.922   +5.056 s     si           +70.482 s    10:01:16.404
nodo-2        09:59:57.918   -2.948 s     si           +78.486 s    10:01:16.404
nodo-3        10:05:00.910   +300.043 s   si           -224.505 s   10:01:16.404
promedio de diferencias: +75.538 s

Escenario 5 - promedio tolerante a fallas (umbral 30 s)
coordinador   10:00:00.932   +0.000 s     si           +0.696 s     10:00:01.628
nodo-1        10:00:05.973   +5.041 s     si           -4.345 s     10:00:01.628
nodo-2        09:59:57.979   -2.954 s     si           +3.649 s     10:00:01.628
nodo-3        10:05:00.977   +300.045 s   NO (atipico) -299.349 s   10:00:01.628
promedio de diferencias: +0.696 s
```

Con el promedio simple, el nodo desfasado **arrastra a todo el grupo**: la diferencia de 300 s pesa un cuarto en el promedio, así que tres relojes que estaban dentro de ±5 s saltan **75,5 s hacia adelante** para encontrarse con él a mitad de camino. Quedan sincronizados entre sí (dispersión de 56 ms en la ronda 2), pero todos con 75 s de error. Con el promedio tolerante a fallas, nodo-3 se marca como atípico y queda fuera del promedio (+0,696 s, casi el de los tres relojes sanos), pero igual recibe su corrección de −299,3 s y se integra al grupo.

**Escenario 6: desconexión de un participante.** `nodo-3` se detiene después de la ronda 1 y se reinicia antes de la ronda 4:

```text
===== Ronda 2 =====
coordinador   10:00:10.174   +0.000 s     si           -0.030 s     10:00:10.144
nodo-1        10:00:10.113   -0.061 s     si           +0.031 s     10:00:10.144
nodo-2        10:00:10.145   -0.030 s     si           -0.001 s     10:00:10.144
nodo-3        SIN RESPUESTA  -            no           -            -  (timeout)
===== Ronda 4 =====   (nodo-3 vuelve con su reloj original: +4 s)
coordinador   10:00:22.253   +0.000 s     si           +0.509 s     10:00:22.762
nodo-1        10:00:22.252   -0.000 s     si           +0.510 s     10:00:22.762
nodo-2        10:00:22.267   +0.014 s     si           +0.496 s     10:00:22.762
nodo-3        10:00:24.277   +2.024 s     si           -1.515 s     10:00:22.762
```

Mientras nodo-3 no responde, el coordinador espera el timeout (2 s), lo excluye de la ronda y sincroniza al resto sin problemas: el algoritmo **degrada sin detenerse**. Cuando nodo-3 vuelve, se reinicia con su desfase original y vuelve a entrar al promedio. Como el promedio es simple, sus +2 s **corren a todos los nodos sanos +0,5 s**: otro ejemplo de por qué conviene el promedio tolerante a fallas, o hacer que un nodo que se reincorpora primero se sincronice sin votar.

**Escenario 7: falla del coordinador.** Se detuvo el coordinador después de la ronda 3, con `nodo-2` derivando 20000 ppm (20 ms por segundo), y se leyeron los relojes desde el host:

```text
Con coordinador (tras la ronda 3)  nodo-1 10:00:14.756 (+0 ms) | nodo-2 10:00:14.795 (+40 ms)  | nodo-3 (+9 ms)  | dispersion 40 ms
--- se detiene el coordinador ---
Sin coordinador, +5 s              nodo-1 10:00:20.566 (+0 ms) | nodo-2 10:00:20.719 (+154 ms) | nodo-3 (+7 ms)  | dispersion 154 ms
Sin coordinador, +10 s             nodo-1 10:00:25.801 (+0 ms) | nodo-2 10:00:26.073 (+272 ms) | nodo-3 (+19 ms) | dispersion 272 ms
Sin coordinador, +20 s             nodo-1 10:00:36.100 (+0 ms) | nodo-2 10:00:36.578 (+478 ms) | nodo-3 (+19 ms) | dispersion 478 ms
```

Los nodos siguen funcionando, pero nadie los corrige: la dispersión crece al ritmo de la deriva, unos 20 ms por segundo, de 40 a 478 ms en 20 s.

**¿Qué diferencias existen entre Cristian y Berkeley?**
Cristian es **cliente–servidor**: cada cliente le *pide* la hora a un servidor que se supone correcto (con acceso a UTC) y la copia, así que es **sincronización externa** y solo se ajustan los clientes. Berkeley es **coordinado**: el coordinador *sondea* a todos, nadie tiene la hora correcta, se calcula el **promedio** y **todos se ajustan**, el coordinador incluido. Es **sincronización interna**: el grupo puede quedar de acuerdo pero lejos de UTC, como en los escenarios 1 a 3, donde todos quedaron entre 1 y 4 s por delante del coordinador. Cristian manda la hora; Berkeley manda ajustes relativos. Berkeley puede filtrar relojes defectuosos con el promedio tolerante a fallas; Cristian copia lo que diga el servidor, esté bien o mal. Ver también la tabla de la pregunta teórica 7.

**¿Qué sucede cuando un nodo presenta un reloj considerablemente desfasado?**
Con el **promedio simple**, ese reloj **contamina el resultado**: su diferencia entra con el mismo peso que las demás y mueve a todo el grupo hacia él. Con 5 minutos de desfase en uno de cuatro relojes, todos saltaron +75,5 s, una corrección enorme y equivocada para tres relojes que estaban bien. Además, un salto así hacia adelante (o hacia atrás para el nodo desfasado) puede romper timeouts, vencimientos y el orden de los logs. La solución es el **promedio tolerante a fallas** del algoritmo original: considerar solo los relojes que no difieren entre sí (o de la mediana) más que un umbral. En el escenario 5 el nodo quedó fuera del promedio (+0,696 s) y aun así se corrigió. Otras opciones son usar la **mediana** directamente, limitar el tamaño máximo de un ajuste, o corregir gradualmente (*slewing*) en vez de saltar.

**¿Qué ocurre si el coordinador deja de funcionar?**
Se detiene la sincronización: no hay quién sondee ni calcule ajustes. Los nodos **siguen funcionando** con su último ajuste, pero sus relojes **vuelven a derivar** y la dispersión crece sin control (escenario 7: de 40 a 478 ms en 20 s con un nodo de 20 ms/s). Lo que dependa de que los relojes coincidan (orden de logs, vencimientos, *leases*) se degrada de a poco. El coordinador es un punto único de falla.

**¿Qué modificaciones permitirían tolerar la falla del coordinador?**
- **Elección de un nuevo coordinador**: los nodos detectan la falla (no llega la ronda esperada dentro de un plazo, o fallan *heartbeats*) y ejecutan un algoritmo de elección, como el del matón (*bully*), el de anillo o Raft. Cualquier nodo puede ser coordinador porque el algoritmo no necesita una fuente de hora especial. El demonio `timed` de Berkeley UNIX ya elegía un nuevo maestro cuando el anterior fallaba.
- **Réplicas en espera**: un coordinador secundario que monitorea al primario y toma su lugar.
- **Sincronización descentralizada**: que cada nodo sondee a los demás y aplique él mismo el promedio tolerante a fallas, sin coordinador (como los algoritmos de promedio distribuido), a costa de más mensajes.
- **Respaldo externo**: que cada nodo, además, use NTP contra varios servidores para no derivar sin límite mientras no hay coordinador.
- **Estado persistente o idempotente**: que los ajustes sean relativos (ya lo son) y que un coordinador nuevo pueda empezar una ronda desde cero sin conocer las anteriores.

## Ejercicio 3 — Relojes lógicos de Lamport

**Implementación.** Tres Web API (`Lamport.Orders`, `Lamport.Inventory` y `Lamport.Payments`) que se comunican por HTTP REST. Cada una tiene un `EventProcess` con su reloj físico, su contador de Lamport y, como herramienta de análisis, un reloj vectorial. Las tres reglas están en `src/Clocks/LogicalClocks.cs`:

```csharp
public long Tick()                    { lock (_lock) return ++_value; }                         // evento interno / envio
public long Receive(long received)    { lock (_lock) { _value = Math.Max(_value, received) + 1; return _value; } }   // recepcion
```

Cada mensaje (petición y respuesta) lleva el reloj en los headers `X-Lamport-Clock` y `X-Vector-Clock`. Los relojes físicos están desfasados a propósito: Orders +0 s, Inventory **−3 s**, Payments **+5 s**. Payments arranca después de procesar 4 pagos de otros pedidos, así que su contador empieza en 4. Se enviaron dos pedidos: `P001` (con stock) y `P003` (sin stock).

**Resultados** (`docs/salidas/ej3_lamport.txt`). Tabla pedida para el primer pedido:

| Paso | Proceso | Evento | Reloj físico | Reloj lógico |
|---|---|---|---|---|
| 1 | OrdersService | Pedido recibido | 10:00:00.091 | 1 |
| 2 | OrdersService | Mensaje enviado a InventoryService | 10:00:00.097 | 2 |
| 3 | InventoryService | Mensaje recibido (recibido L=2 > local 0: salta a 3) | **09:59:57.228** | 3 |
| 4 | InventoryService | Inventario verificado: P001 stock 10, pedido 2 → reservado | 09:59:57.229 | 4 |
| 5 | InventoryService | Respuesta enviada | 09:59:57.229 | 5 |
| — | OrdersService | Respuesta recibida (recibido L=5 > local 2: salta a 6) | 10:00:00.245 | 6 |
| 6 | OrdersService | Solicitud de pago enviada | 10:00:00.252 | 7 |
| 7 | PaymentsService | Solicitud de pago recibida (recibido L=7 > local 4: salta a 8) | 10:00:05.356 | 8 |
| 7 | PaymentsService | Pago procesado: $150.00 aprobado | 10:00:05.408 | 9 |
| 8 | PaymentsService | Confirmación enviada | 10:00:05.408 | 10 |
| — | OrdersService | Confirmación de pago recibida (recibido L=10 > local 7: salta a 11) | **10:00:00.418** | 11 |
| 9 | OrdersService | Pedido ORD-001 confirmado | 10:00:00.419 | 12 |

El segundo pedido continúa la numeración (Orders 13–19, Inventory 15–17) y termina en “Pedido ORD-002 rechazado: sin stock” con L=19.

`GET /api/timeline` junta los registros de los tres servicios y compara los dos órdenes:

```text
=== Orden por reloj fisico (hora local de cada servicio) ===
InventoryService  09:59:57.228   3   Mensaje recibido de OrdersService (pedido ORD-001)
InventoryService  09:59:57.229   4   Inventario verificado ...
...
OrdersService     10:00:00.091   1   Pedido recibido ORD-001       <- la causa aparece DESPUES del efecto
OrdersService     10:00:00.097   2   Mensaje enviado a InventoryService

=== Inconsistencias del reloj fisico: 93 pares donde a -> b pero hora(b) < hora(a) ===
  Orders L=2 "Mensaje enviado a InventoryService"     ->  Inventory L=3 "Mensaje recibido"   (hora(b) - hora(a) = -2.861 s)
  Payments L=10 "Confirmacion enviada a OrdersService" ->  Orders L=11 "Confirmacion recibida" (hora(b) - hora(a) = -4.990 s)
  Orders L=14 "Mensaje enviado a InventoryService"    ->  Inventory L=15 "Mensaje recibido"  (hora(b) - hora(a) = -2.997 s)

=== Eventos concurrentes con L(a) < L(b): 24 pares (Lamport no los distingue) ===
  Orders L=1 [I=0,O=1,P=0] "Pedido recibido ORD-001"  ||  Payments L=2 [I=0,O=0,P=2] "Pago de otro pedido procesado (#2)"
  Orders L=2 [I=0,O=2,P=0] "Mensaje enviado a ..."    ||  Payments L=3 [I=0,O=0,P=3] "Pago de otro pedido procesado (#3)"
```

Ordenado por Lamport, cada envío aparece antes de su recepción (2 → 3, 5 → 6, 7 → 8, 10 → 11, 14 → 15, 17 → 18): ningún par viola la causalidad.

**¿Por qué los valores de Lamport no representan la hora real?**
Porque son **contadores de eventos**, no mediciones de tiempo: solo aumentan cuando ocurre algo (un evento interno, un envío o una recepción), no con el paso del tiempo. No tienen unidad ni relación con UTC: entre L=9 y L=10 de Payments pasaron 0 ms, entre L=1 y L=2 de Orders 6 ms, y entre los dos pedidos casi 50 ms, sin que eso se refleje en los números. Además, al recibir un mensaje el contador salta para respetar la causalidad: Inventory pasó de 0 a 3 sin que ocurrieran tres eventos. Lo único que garantizan es el orden: si *a → b* entonces *L(a) < L(b)*.

**¿Qué ocurre cuando un proceso recibe un mensaje con un valor lógico superior al suyo?**
**Adelanta** su contador para quedar por delante del emisor: *C = max(C, t) + 1 = t + 1*. Así la recepción queda numerada después del envío, aunque el receptor haya tenido menos actividad. En la práctica: Inventory estaba en 0, recibió L=2 y pasó a **3**; Orders estaba en 2, recibió la respuesta con L=5 y pasó a **6**; Payments estaba en 4, recibió L=7 y pasó a **8**. Si el valor recibido es menor o igual al propio, solo se suma 1. Por ejemplo, si Payments hubiera procesado 10 pagos previos (L=10) y recibido L=7, pasaría a 11. Así el reloj nunca retrocede.

**¿Cómo permite Lamport mantener el orden causal de los eventos?**
Garantiza la **condición de reloj**: si *a → b* entonces *L(a) < L(b)*. Dentro de un proceso, cada evento incrementa el contador, así que los eventos posteriores tienen valores mayores. Entre procesos, el mensaje lleva el valor del envío y el receptor queda por encima con max + 1, así que la recepción es mayor que el envío. Por transitividad, cualquier cadena causal tiene valores crecientes. Por eso ordenar por *(L, proceso)* produce un orden total **consistente con la causalidad**, que todos los procesos pueden calcular igual, sin importar sus relojes físicos. En el registro, ese orden puso los 9 pasos del pedido en la secuencia correcta, mientras que el orden físico tenía 93 pares invertidos.

**¿Es posible determinar si dos eventos son concurrentes utilizando únicamente sus marcas de tiempo de Lamport?**
**No.** La implicación vale en un solo sentido: *a → b ⇒ L(a) < L(b)*, pero **L(a) < L(b) no implica a → b**. Dos eventos concurrentes pueden tener cualquier par de valores. En la salida, “Pedido recibido ORD-001” (Orders, L=1) y “Pago de otro pedido #2” (Payments, L=2) cumplen 1 < 2 y sin embargo son **concurrentes**: no hay ningún mensaje que los conecte. Hubo 24 pares así. Lo único que se puede afirmar es que si *L(a) ≥ L(b)*, entonces *a* no ocurrió antes que *b*. Para detectar concurrencia se usan **relojes vectoriales**: cada proceso guarda un contador por proceso y *a → b* si y solo si *V(a) ≤ V(b)* componente a componente (y son distintos). Los vectores `[I=0,O=1,P=0]` y `[I=0,O=0,P=2]` no son comparables, por lo tanto los eventos son concurrentes. Por eso el proyecto los incluye como herramienta de análisis.

## Ejercicio 4 — Comparación de algoritmos

| Característica | Cristian | Berkeley | Lamport |
|---|---|---|---|
| **Tipo de reloj** | Físico | Físico | Lógico (contador) |
| **Objetivo principal** | Acercar la hora de cada cliente a la de un servidor de referencia (sincronización externa, UTC) | Que todos los relojes del grupo coincidan entre sí (sincronización interna) | Ordenar los eventos respetando la causalidad (*happens-before*) |
| **Requiere servidor central** | Sí: el servidor de tiempo | Sí: el coordinador (elegible, no tiene la hora “correcta”) | No: cada proceso mantiene su contador |
| **Utiliza mensajes** | Sí: petición y respuesta (2 por sincronización y cliente) | Sí: sondeo y ajuste (≈3 por nodo y ronda) | Sí, pero no mensajes propios: el contador viaja dentro de los mensajes de la aplicación (un header) |
| **Sincroniza hora física** | Sí, con precisión ±RTT/2 (0,1 ms en Docker, −201 ms con latencia asimétrica) | Sí, entre los participantes; el promedio puede alejarse de UTC | No: no tiene relación con la hora real |
| **Establece orden causal** | No: tras sincronizar puede seguir habiendo eventos invertidos más cercanos que la precisión | No, por la misma razón | Sí: si a → b entonces L(a) < L(b); no detecta concurrencia (para eso, relojes vectoriales) |
| **Comportamiento ante fallas** | Si cae el servidor, nadie sincroniza y los relojes vuelven a derivar (B: +53 ms en ~10 s); se descartan las muestras con RTT alto; un servidor con hora errónea contagia a todos | Un nodo caído se excluye de la ronda; un reloj muy desviado arrastra el promedio salvo con el promedio tolerante a fallas; si cae el coordinador nadie sincroniza hasta elegir otro | No depende de un servidor; si un proceso cae, el resto sigue. Exige que los mensajes lleven el contador; un proceso que reinicia en 0 debe persistir su contador o recibirlo de los demás |

**1. ¿Qué algoritmo utilizaría para sincronizar los relojes de varios servidores?**
Si hay acceso a una referencia confiable de UTC, el enfoque de **Cristian**, en la práctica **NTP** (que es Cristian refinado: varias muestras, varios servidores, filtrado y corrección gradual) o **PTP** (IEEE 1588) si se necesita precisión de microsegundos. Es lo que se usa en cualquier centro de datos. Si los servidores están aislados y no tienen referencia externa, o lo único que importa es que coincidan entre ellos, **Berkeley** con promedio tolerante a fallas y elección de coordinador. Lo que no haría es depender de un único servidor de tiempo sin redundancia.

**2. ¿Qué algoritmo utilizaría para establecer el orden causal de operaciones entre microservicios?**
**Relojes lógicos**: **Lamport** si alcanza con un orden total consistente con la causalidad (por ejemplo, para ordenar eventos en un log, versionar operaciones o desempatar en exclusión mutua distribuida), o **relojes vectoriales** si además hay que detectar operaciones concurrentes (conflictos en réplicas, como hacen Dynamo o Riak). No usaría relojes físicos para esto: el ejercicio 3 mostró 93 pares de eventos invertidos con desfases de apenas 3 y 5 s. Además, el contador viaja en un header, sin servidores ni mensajes extra, lo que encaja naturalmente en HTTP o en colas de mensajes. Lo mismo hacen en la práctica los identificadores de trazas distribuidas o los *hybrid logical clocks* de bases como CockroachDB.

**3. ¿Qué diferencias existen entre sincronización física y sincronización lógica?**
La **física** ajusta relojes que miden tiempo real para que coincidan con UTC o entre sí, siempre con un error residual (±RTT/2, deriva entre sincronizaciones), y sirve para todo lo que necesita la hora o las duraciones: auditoría, vencimientos, timeouts, métricas. La **lógica** no mide tiempo: numera eventos para capturar la relación *happens-before*, es exacta respecto de la causalidad, no necesita servidores ni mensajes de sincronización y no sufre deriva, pero no dice qué hora es ni cuánto tardó algo. La física responde “¿cuándo?”, la lógica responde “¿qué pasó antes?”. Ver también la tabla de la pregunta 11.

**4. ¿Es posible combinar diferentes algoritmos en una misma arquitectura distribuida? Justifique.**
Sí, y es lo habitual, porque resuelven problemas distintos y complementarios:
- **NTP (Cristian) + Berkeley**: los servidores se sincronizan con UTC vía NTP y, dentro de un clúster aislado, un *time daemon* tipo Berkeley mantiene la coherencia interna si se pierde la referencia externa.
- **Relojes físicos + lógicos**: la plataforma de comercio de este caso usaría NTP para que las fechas de pedidos, facturas y logs sean razonables para las personas y para los vencimientos, y Lamport o vectoriales en los mensajes entre `OrdersService`, `InventoryService` y `PaymentsService` para que el orden de las operaciones sea correcto aunque los relojes difieran.
- **Relojes híbridos (HLC)**: combinan las dos ideas en una sola marca, *(hora física, contador lógico)*. Se mantienen cerca de la hora real pero garantizan la condición de reloj de Lamport. Los usan bases de datos distribuidas como CockroachDB y MongoDB. Google Spanner va más allá con TrueTime: relojes atómicos y GPS que acotan la incertidumbre y esperan a que pase.

La condición es usar cada mecanismo para lo que sirve: nunca ordenar causalmente por hora física, y nunca sacar duraciones u horas de un reloj lógico.

## Tests

`dotnet test tests/Clocks.Tests` ejecuta 10 tests de la lógica pura (sin red): estimación de Cristian y selección de la mejor muestra, promedio de Berkeley con nodo atípico, promedio tolerante a fallas y nodo desconectado, reglas de Lamport (salto al recibir un valor mayor y +1 al recibir uno menor) y detección de concurrencia con relojes vectoriales. Resultado en `docs/salidas/tests.txt`.

## Bibliografía

1. Tanenbaum, A. S. y Van Steen, M. *Distributed Systems*, cap. 6 “Coordination”: sincronización de relojes, relojes lógicos, relojes vectoriales.
2. Coulouris, G., Dollimore, J., Kindberg, T. y Blair, G. *Distributed Systems: Concepts and Design*, cap. 14 “Time and Global States”.
3. Cristian, F. (1989). *Probabilistic clock synchronization*. Distributed Computing 3(3).
4. Gusella, R. y Zatti, S. (1989). *The accuracy of the clock synchronization achieved by TEMPO in Berkeley UNIX 4.3BSD*. IEEE TSE 15(7).
5. Lamport, L. (1978). *Time, clocks, and the ordering of events in a distributed system*. Communications of the ACM 21(7).
6. Mills, D. et al. RFC 5905 — *Network Time Protocol Version 4*.
7. Microsoft Learn — ASP.NET Core Minimal APIs, `HttpClient`, Docker para .NET.
