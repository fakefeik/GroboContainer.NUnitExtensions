using System.Reflection;

using GroboContainer.NUnitExtensions.Impl.TestContext;

namespace GroboContainer.NUnitExtensions.Tests.ExecutionOrder
{
    [WithX("1")]
    public class WithY : EdiTestSuiteWrapperAttribute
    {
        public WithY(string q)
        {
            this.q = q;
        }

        public override void SetUp(string suiteName, Assembly testAssembly, IEditableEdiTestContext suiteContext)
        {
            EdiTestMachineryTrace.Log(string.Format("WithY(q={0}).SetUp()", q), suiteContext);
        }

        public override void TearDown(string suiteName, Assembly testAssembly, IEditableEdiTestContext suiteContext)
        {
            EdiTestMachineryTrace.Log(string.Format("WithY(q={0}).TearDown()", q), suiteContext);
        }

        protected override string TryGetIdentity()
        {
            return q;
        }

        private readonly string q;
    }
}