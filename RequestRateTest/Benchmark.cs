using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RequestRateTest
{
    public class Benchmark<T>
    {
        private readonly Func<T, Task> _consumer;
        private readonly CancellationTokenSource _cts;
        private readonly Func<IEnumerable<T>> _producer;
        private readonly Channel<T> _queue;
        private readonly List<Task> _workers;
        private Task _producerTask;
        private int _stat;
        public Task RunerTask;

        public Benchmark(Func<IEnumerable<T>> requestParamsProducer, Func<T, Task> requestExecutor, int queueLength,
            int threadsCount, TimeSpan? duration = null, TimeSpan? statInterval = null)
        {
            duration ??= TimeSpan.FromSeconds(60);
            statInterval ??= TimeSpan.FromSeconds(5);
            _producer = requestParamsProducer;
            _consumer = requestExecutor;
            _cts = new CancellationTokenSource();
            _queue = Channel.CreateBounded<T>(queueLength);
            _producerTask = ProducerAsync(_queue.Writer, _cts.Token);
            _workers = new List<Task>(threadsCount);
            for (var i = 0; i < threadsCount; i++) _workers.Add(WorkerAsync(_queue.Reader, _cts.Token));

            RunerTask = Runer(duration.Value, statInterval.Value);
        }

        private async Task Runer(TimeSpan duration, TimeSpan statInterval)
        {
            var startTime = DateTime.Now;
            var stopwatch = new Stopwatch();
            int prevStat;
            while (DateTime.Now - duration < startTime)
            {
                prevStat = _stat;
                stopwatch.Start();
                await Task.Delay(5000);
                stopwatch.Stop();
                var rps = (_stat - prevStat) / (stopwatch.ElapsedMilliseconds / 1000.0);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}|Requests per second: {rps:0.00}\t Total: {_stat}");
                stopwatch.Reset();
            }

            _cts.Cancel();
        }

        private async Task ProducerAsync(ChannelWriter<T> writer, CancellationToken ctx)
        {
            try
            {
                var data = _producer();
                foreach (var newRequestData in data)
                {
                    ctx.ThrowIfCancellationRequested();
                    await writer.WriteAsync(newRequestData, ctx).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        public async Task WorkerAsync(ChannelReader<T> reader, CancellationToken ctx)
        {
            try
            {
                while (true)
                {
                    ctx.ThrowIfCancellationRequested();
                    var requestParams = await reader.ReadAsync(ctx).ConfigureAwait(false);
                    await _consumer(requestParams);
                    Interlocked.Add(ref _stat, 1);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
    }
}
