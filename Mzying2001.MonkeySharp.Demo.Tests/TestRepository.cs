using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Repository;
using System.Threading;

namespace Mzying2001.MonkeySharp.Demo.Tests
{
    internal static class TestRepository
    {
        internal static UserScriptRepository Create(IUserScriptMetadataParser parser = null, UserScriptPreparer preparer = null)
        {
            var repository = new UserScriptRepository(new InMemoryUserScriptPersistence(), parser, preparer);
            repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return repository;
        }
    }
}
