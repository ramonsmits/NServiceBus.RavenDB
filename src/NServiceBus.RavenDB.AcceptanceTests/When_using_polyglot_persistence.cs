namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvanceExtensibility;
    using NServiceBus.Features;
    using NServiceBus.Outbox;
    using NServiceBus.Persistence;
    using NServiceBus.Sagas;
    using NServiceBus.Timeout.Core;
    using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;
    using NUnit.Framework;

    public class When_using_polyglot_persistence : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_throw()
        {
            var test = Scenario.Define<Context>()
                .WithEndpoint<PolyglotEndpoint>(b =>
                {
                    b.CustomConfig((cfg, c) =>
                    {
                        var settings = cfg.GetSettings();
                        var persistence = ConfigureEndpointRavenDBPersistence.GetDefaultPersistenceExtensions(settings);

                        persistence.ResetDocumentStoreSettings(out var dbInfo);

                        cfg.UsePersistence<RavenDBPersistence, StorageType.Subscriptions>().SetDefaultDocumentStore(dbInfo.DocStore);

                        cfg.UsePersistence<InMemoryPersistence, StorageType.Sagas>();
                        cfg.UsePersistence<InMemoryPersistence, StorageType.Timeouts>();
                        cfg.UsePersistence<InMemoryPersistence, StorageType.Outbox>();

                        cfg.EnableFeature<TimeoutManager>();
                        cfg.EnableOutbox();
                    });

                    b.When(async (session, ctx) => await session.SendLocal(new TestMsg
                    {
                        RunId = ctx.TestRunId
                    }));
                })
                .Done(c => c.Done);

            var context = await test.Run();
            Assert.IsTrue(context.EndpointsStarted);
            Assert.AreEqual("NServiceBus.InMemorySynchronizedStorageSession", context.SyncSessionType);
            Assert.AreEqual("NServiceBus.Persistence.RavenDB.SubscriptionPersister", context.SubscriptionsType);
            Assert.AreEqual("NServiceBus.InMemorySagaPersister", context.SagasType);
            Assert.AreEqual("NServiceBus.InMemoryOutboxStorage", context.OutboxType);
            Assert.AreEqual("NServiceBus.InMemoryTimeoutPersister", context.TimeoutsType);
        }

        public class Context : ScenarioContext
        {
            public bool Done { get; set; }
            public string SubscriptionsType { get; set; }
            public string SagasType { get; set; }
            public string OutboxType { get; set; }
            public string TimeoutsType { get; set; }
            public string SyncSessionType { get; set; }
        }

        public class PolyglotEndpoint : EndpointConfigurationBuilder
        {
            public PolyglotEndpoint()
            {
                EndpointSetup<DefaultServer>();
            }

            public class JustASaga : Saga<JustASagaData>,
                IAmStartedByMessages<TestMsg>
            {
                Context testCtx;

                public JustASaga(Context testCtx, ISubscriptionStorage subs, ISagaPersister saga, IOutboxStorage outbox, IQueryTimeouts timeouts)
                {
                    this.testCtx = testCtx;
                    testCtx.SubscriptionsType = subs.GetType().FullName;
                    testCtx.SagasType = saga.GetType().FullName;
                    testCtx.OutboxType = outbox.GetType().FullName;
                    testCtx.TimeoutsType = timeouts.GetType().FullName;
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<JustASagaData> mapper)
                {
                    mapper.ConfigureMapping<TestMsg>(m => m.RunId).ToSaga(s => s.RunId);
                }

                public Task Handle(TestMsg message, IMessageHandlerContext context)
                {
                    var syncSession = context.SynchronizedStorageSession;
                    testCtx.SyncSessionType = syncSession.GetType().FullName;
                    testCtx.Done = true;
                    return Task.FromResult(0);
                }
            }

            public class JustASagaData : ContainSagaData
            {
                public virtual Guid RunId { get; set; }
            }
        }

        public class TestMsg : ICommand
        {
            public Guid RunId { get; set; }
        }
    }
}