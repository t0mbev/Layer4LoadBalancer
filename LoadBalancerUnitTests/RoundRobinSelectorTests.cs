using System.Net;
using LoadBalancer.BackendSelectors;

namespace LoadBalancerUnitTests
{
    [TestFixture]
    public class RoundRobinSelectorTests
    {
        [Test]
        public void Next_CyclesThroughBackends_ReturnsExpectedIndices()
        {
            var selector = new RoundRobinSelector();

            var backends = new[]
            {
                new LoadBalancer.Backend(new IPEndPoint(IPAddress.Loopback, 1), true),
                new LoadBalancer.Backend(new IPEndPoint(IPAddress.Loopback, 2), true),
                new LoadBalancer.Backend(new IPEndPoint(IPAddress.Loopback, 3), true),
            };

            Assert.That(selector.Next(backends, null), Is.EqualTo(0));
            Assert.That(selector.Next(backends, null), Is.EqualTo(1));
            Assert.That(selector.Next(backends, null), Is.EqualTo(2));
            Assert.That(selector.Next(backends, null), Is.EqualTo(0));
        }

        [Test]
        public void Next_WrapsWhenSingleBackend_ReturnsZeroAlways()
        {
            var selector = new RoundRobinSelector();
            var backends = new[] { new LoadBalancer.Backend(new IPEndPoint(IPAddress.Loopback, 1234), true) };
            Assert.That(selector.Next(backends, null), Is.EqualTo(0));
            Assert.That(selector.Next(backends, null), Is.EqualTo(0));
        }

        [Test]
        public void Next_ThrowsWhenBackendsEmpty_ThrowsException()
        {
            var selector = new RoundRobinSelector();
            var backends = Array.Empty<LoadBalancer.Backend>();
            Assert.Throws<DivideByZeroException>(() => selector.Next(backends, null));
        }
    }
}
