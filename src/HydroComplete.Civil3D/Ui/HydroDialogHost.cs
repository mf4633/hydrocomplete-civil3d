using System.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HydroComplete.Civil3D.Ui
{
    /// <summary>Shows modal WPF dialogs safely inside the Civil 3D process.</summary>
    internal static class HydroDialogHost
    {
        public static bool? Show(Window window)
        {
            if (window == null) return false;

            try
            {
                return AcadApp.ShowModalWindow(window);
            }
            catch
            {
                return window.ShowDialog();
            }
        }
    }
}