# VkNet.ExecuteExtension
Расширение для библиотеки [VkNet](https://github.com/vknet/vk) реализующее упаковку запросов через метод [Execute](https://vk.com/dev/execute).
## Описание
Перехват запроса и его упаковка происходи на уровне вызова метода `Call` внутри класса `VkApi` из библиотеки [VkNet](https://github.com/vknet/vk). Все вызовы методов за исключением, указанных в параметре `SkipMethods`, перехватываются для упаковки включая обычные и асинхронные их версии.

***Execute метод имеет ограничение на объем ответа = 5 МБ.*** Для оптимальной работы иногда придётся подбирать веса. 

При вызове любого метода API запросу, который будет упакован присваивается вес равный `DefaultMethodWeight` или особому весу, если он определён в `MethodsWeight` для данного метода. `MaxExecute` устанавливает максимальный суммарный вес запросов в методе Execute. Эти три параметра отвечают за плотность упаковки и подбираются индивидуально.

При возникновении ошибки "response size is too big" вес всех упакованных подзапросов увеличивается на 1, и они возвращаются в очередь на упаковку.
## Пример
### Инициализация начальных параметров
```csharp
VkApiExecute vk = new VkApiExecute();
vk.Authorize(new ApiAuthParams { AccessToken = "Your Token" });

vk.MaxExecute = 25;                                                     //Максимальный суммарный вес методов при вызове Execute. <=25
vk.DefaultMethodWeight = 1;                                             //Стандартный начальный вес методов
vk.MethodsWeight = new Dictionary<string, int>() { { "wall.get", 3 } }; //Особые начальные веса для методов
vk.SkipMethods = new HashSet<string>() { "groups.getMembers" };         //Методы, которые не нужно упаковывать

vk.MaxWaitingTime = TimeSpan.FromSeconds(5);                            //Максимальное время ожидания для запроса
vk.PendingTime = TimeSpan.FromSeconds(1);                               //Время ожидания новых запросов

```
### Вызов методов
При вызове методов желательно использовать параметр TaskCreationOptions.LongRunning. 
```csharp
var task = Task.Factory.StartNew(() => vk.Wall.Get(new WallGetParams()
{
    Domain = "spacex",
    Count = 100,
    Offset = 0
}), TaskCreationOptions.LongRunning);
await task;
```
Или так
```csharp
var task = new Task<WallGetObject>(() => vk.Wall.Get(new WallGetParams()
{
    Domain = "spacex",
    Count = 100,
    Offset = 0
}), TaskCreationOptions.LongRunning);
task.Start();
await task;
```
Без TaskCreationOptions.LongRunning возможны ухудшение производительности на начальных этапах, когда ThreadPool не выделил ещё достаточное число потоков.

Вызов методов ничем не отличается от библиотеки [VkNet](https://github.com/vknet/vk).
```csharp
var task =Task.Run(() => vk.Wall.Get(new WallGetParams(){...}));
await task;
```
```csharp
var task = vk.Wall.GetAsync(new WallGetParams(){...});
await task;
```
### Подборка весов
При необходимости для повышения производительности можно подобрать оптимальные веса каждого метода API. Самый простой способ подобрать их через логи.
```csharp
var config = new LoggingConfiguration();
var logfile = new FileTarget("logfile") { FileName = "TestLogs.txt", DeleteOldFileOnStartup = true };
config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
LogManager.Configuration = config;
Logger log = LogManager.GetCurrentClassLogger();
VkApiExecute vk = new VkApiExecute(log);
...
```





