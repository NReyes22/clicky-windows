// Disambiguate types that conflict between WPF (System.Windows) and WinForms (System.Drawing).
// WPF types are the default; WinForms types are accessed via their full namespace.

global using Point = System.Windows.Point;
global using Application = System.Windows.Application;
global using Color = System.Windows.Media.Color;
global using UserControl = System.Windows.Controls.UserControl;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Canvas = System.Windows.Controls.Canvas;
