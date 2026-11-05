using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using AnimationSystemDOTS.Editor.Baking;

namespace AnimationSystemDOTS.Tests
{
    public class CachePublicationTests
    {
        [Test]
        public void ParallelPublish_AllWritersSucceedAndDestinationIsACompletePayload()
        {
            var allowed = Path.GetFullPath("Artifacts/CachePublication") + Path.DirectorySeparatorChar;
            var directory = Path.Combine(allowed, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                for (var round = 0; round < 5; round++)
                {
                    var destination = Path.Combine(directory, round + ".json");
                    var contents = Enumerable.Range(0, 8).Select(i => new string((char)('a' + i), 65536)).ToArray();
                    var tasks = new Task[contents.Length];
                    using (var ready = new CountdownEvent(tasks.Length))
                    using (var start = new ManualResetEventSlim())
                    {
                        for (var i = 0; i < tasks.Length; i++)
                        {
                            var source = Path.Combine(directory, round + "-" + i + ".tmp");
                            File.WriteAllText(source, contents[i]);
                            tasks[i] = Task.Factory.StartNew(() =>
                            {
                                ready.Signal(); start.Wait();
                                AnimationBlobCache.Publish(source, destination);
                            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        }
                        var allReady = ready.Wait(TimeSpan.FromSeconds(10));
                        start.Set();
                        Assert.That(Task.WaitAll(tasks, TimeSpan.FromSeconds(10)), Is.True);
                        Assert.That(allReady, Is.True);
                    }
                    Assert.That(contents, Does.Contain(File.ReadAllText(destination)), "Atomic publication must preserve one complete writer payload.");
                }
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            }
            finally
            {
                if (!Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Publication test cleanup escaped its workspace.");
                Directory.Delete(directory, true);
            }
        }
    }
}
