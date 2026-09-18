using System.Threading.Tasks;

namespace NzbDrone.Core.Update;

public interface IPostUpdateVerificationService
{
    UpdateState GetUpdateState();

    UpdateState StageUpdate(string targetVersion, string backupPath);

    Task<bool> VerifyUpdateAsync();

    Task RollbackAsync(string reason);
}
