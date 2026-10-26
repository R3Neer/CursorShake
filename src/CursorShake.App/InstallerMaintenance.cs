using CursorShake.Windows.Lifecycle;
using CursorShake.Windows.Recovery;
using System.Security.Principal;

namespace CursorShake.App;

internal enum MaintenanceAction { Quiesce, ProvisionIndex, UninstallKeepData, UninstallRemoveData }
internal sealed record MaintenanceRequest(MaintenanceAction Action,string InstallationRoot);
internal static class InstallerMaintenance
{
    internal static MaintenanceRequest? Parse(string[] arguments)
    {
        if(arguments.Length!=2 || !Path.IsPathFullyQualified(arguments[1]))return null;
        MaintenanceAction? action=arguments[0] switch
        {
            "--installer-quiesce"=>MaintenanceAction.Quiesce,
            "--installer-provision-index"=>MaintenanceAction.ProvisionIndex,
            "--installer-uninstall-keep-data"=>MaintenanceAction.UninstallKeepData,
            "--installer-uninstall-remove-data"=>MaintenanceAction.UninstallRemoveData,
            _=>null
        };
        return action is {} value?new(value,Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments[1]))):null;
    }
    internal static void Execute(MaintenanceRequest request)
    {
        // Validate uninstall identity before requesting any other instance to stop.
        if(request.Action!=MaintenanceAction.Quiesce && !string.Equals(request.InstallationRoot,Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Uninstall maintenance must run from the installation being removed.");
        if(request.Action==MaintenanceAction.ProvisionIndex)
        {
            InstallationGate.AssertMaintenanceHeld(request.InstallationRoot);
            InstallationRecoveryIndex.Provision(request.InstallationRoot);
            return;
        }
        bool elevated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        if(request.Action==MaintenanceAction.UninstallRemoveData && elevated)
            throw new InvalidOperationException("Una desinstalación elevada conserva los datos de todas las cuentas.");
        InstallationProcesses.QuiesceAsync(request.InstallationRoot).GetAwaiter().GetResult();
        if(request.Action==MaintenanceAction.Quiesce)
        {
            InstallationRecoveryIndex.AssertBeforeUpdate(request.InstallationRoot);
            return;
        }
        InstallationRecoveryIndex.AssertClear(request.InstallationRoot);
        PersonalDataRemovalPlan? plan=null;
        if(request.Action==MaintenanceAction.UninstallRemoveData)
        {
            plan=PersonalDataRemoval.PrepareCurrentUser();
            if(plan.Paths.Count>0 && MessageBox.Show("Se eliminarán únicamente estos datos de CursorShake:\n\n"+string.Join("\n",plan.Paths)+"\n\nLa carpeta contenedora y los archivos ajenos se conservan. ¿Continuar?",
                "Eliminar datos de CursorShake",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
                throw new OperationCanceledException("El usuario ha cancelado la eliminación de sus datos; la aplicación permanece instalada.");
        }
        InstalledStartupTasks.RemoveForInstallation(request.InstallationRoot);
        plan?.Delete();
    }
}
