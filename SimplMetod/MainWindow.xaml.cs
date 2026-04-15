using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SimplMetod
{
    /// <summary>
    /// Главное окно приложения для решения задач линейного программирования
    /// с помощью двухфазного симплекс-метода.
    /// </summary>
    public partial class MainWindow : Window
    {
        private int numVariables = 3;
        private int numConstraints = 3;

        private TextBox[] targetBoxes;
        private List<ConstraintRow> constraintRows = new List<ConstraintRow>();

        /// <summary>
        /// Конструктор главного окна.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            GenerateUI();
        }

        /// <summary>
        /// Обновляет интерфейс при изменении количества переменных или ограничений.
        /// </summary>
        private void UpdateInterface_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtNumVariables.Text, out numVariables) || numVariables <= 0)
                numVariables = 3;
            if (!int.TryParse(TxtNumConstraints.Text, out numConstraints) || numConstraints <= 0)
                numConstraints = 3;

            GenerateUI();
        }

        /// <summary>
        /// Генерирует динамический интерфейс для целевой функции и ограничений.
        /// </summary>
        private void GenerateUI()
        {
            TargetFunctionPanel.Children.Clear();
            ConstraintsPanel.Children.Clear();
            constraintRows.Clear();

            targetBoxes = new TextBox[numVariables];

            // Целевая функция
            for (int j = 0; j < numVariables; j++)
            {
                targetBoxes[j] = new TextBox
                {
                    Width = 55,
                    Margin = new Thickness(3),
                    Text = "0",
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                TargetFunctionPanel.Children.Add(targetBoxes[j]);
                TargetFunctionPanel.Children.Add(new TextBlock
                {
                    Text = $"x{j + 1}" + (j < numVariables - 1 ? " + " : ""),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 8, 0)
                });
            }

            // Ограничения
            for (int i = 0; i < numConstraints; i++)
            {
                var row = new ConstraintRow(numVariables);
                ConstraintsPanel.Children.Add(row.Root);
                constraintRows.Add(row);
            }
        }

        /// <summary>
        /// Проверяет корректность введённых данных.
        /// </summary>
        private bool ValidateInput()
        {
            for (int i = 0; i < targetBoxes.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(targetBoxes[i].Text) || !TryParse(targetBoxes[i].Text))
                {
                    TxtResult.Text = $"Ошибка в коэффициенте целевой функции x{i + 1}.";
                    return false;
                }
            }

            foreach (var row in constraintRows)
            {
                foreach (var box in row.CoeffBoxes)
                {
                    if (string.IsNullOrWhiteSpace(box.Box.Text) || !TryParse(box.Box.Text))
                    {
                        TxtResult.Text = "Ошибка в коэффициентах ограничений.";
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(row.RightBox.Box.Text) || !TryParse(row.RightBox.Box.Text))
                {
                    TxtResult.Text = "Ошибка в правой части ограничений.";
                    return false;
                }

                if (row.Sign.SelectedItem == null)
                {
                    TxtResult.Text = "Не выбран знак ограничения.";
                    return false;
                }
            }
            return true;
        }

        private bool TryParse(string text)
        {
            return double.TryParse(text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out _);
        }

        private double ParseDouble(string text)
        {
            if (double.TryParse(text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                return val;
            throw new Exception($"Некорректное число: {text}");
        }

        /// <summary>
        /// Запускает двухфазный симплекс-метод.
        /// </summary>
        private void Solve_Click(object sender, RoutedEventArgs e)
        {
            TxtResult.Clear();
            try
            {
                if (!ValidateInput()) return;
                RunTwoPhaseSimplex();
            }
            catch (Exception ex)
            {
                TxtResult.Text = $"Ошибка: {ex.Message}";
            }
        }

        // ====================== ОСНОВНАЯ ЛОГИКА СИМПЛЕКС-МЕТОДА ======================
        private void RunTwoPhaseSimplex()
        {
            int vars = numVariables;
            int cons = numConstraints;

            double[] c = new double[vars];
            for (int j = 0; j < vars; j++)
                c[j] = ParseDouble(targetBoxes[j].Text);

            bool maximize = (CbOptimize.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Максимизировать";

            double[,] A = new double[cons, vars];
            double[] b = new double[cons];
            int[] signs = new int[cons];

            for (int i = 0; i < cons; i++)
            {
                for (int j = 0; j < vars; j++)
                    A[i, j] = constraintRows[i].CoeffBoxes[j].GetValue();
                b[i] = constraintRows[i].RightBox.GetValue();

                string signStr = constraintRows[i].Sign.SelectedItem.ToString();
                signs[i] = signStr.Contains("<=") ? -1 : signStr.Contains(">=") ? 1 : 0;
            }

            TxtResult.Text += "=== ДВУХФАЗНЫЙ СИМПЛЕКС-МЕТОД ===\n\n";

            // Фаза I
            TxtResult.Text += "=== ФАЗА I — Поиск допустимого решения ===\n\n";
            BuildPhase1Tableau(A, b, signs, vars, cons, out double[,] T, out int m, out int n, out List<int> basis);
            PrintTable("Начальная таблица Фазы I:", T, m, n, basis);

            if (!SimplexProcess(T, m, n, basis, true)) return;

            if (Math.Abs(T[m - 1, n - 1]) > 1e-8)
            {
                TxtResult.Text += "\nЗадача не имеет допустимого решения.\n";
                return;
            }

            TxtResult.Text += "\nФаза I завершена успешно. Переход к Фазе II.\n\n";

            // Фаза II
            TxtResult.Text += "=== ФАЗА II — Оптимизация ===\n\n";
            BuildPhase2Objective(T, m, n, c, maximize, vars, basis);
            PrintTable("Начальная таблица Фазы II:", T, m, n, basis);

            if (!SimplexProcess(T, m, n, basis, false)) return;

            PrintTable("Финальная таблица:", T, m, n, basis);

            double[] solution = new double[vars];
            for (int i = 0; i < basis.Count; i++)
            {
                if (basis[i] < vars)
                    solution[basis[i]] = T[i, n - 1];
            }

            TxtResult.Text += "\n=== ОПТИМАЛЬНОЕ РЕШЕНИЕ ===\n\n";
            for (int j = 0; j < vars; j++)
                TxtResult.Text += $"x{j + 1} = {solution[j]:0.###}\n";
            TxtResult.Text += $"\nОптимум = {T[m - 1, n - 1]:0.###}\n";
        }

        // ====================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ======================
        private void BuildPhase1Tableau(double[,] A, double[] b, int[] signs, int vars, int cons,
            out double[,] T, out int m, out int n, out List<int> basis)
        {
            int slack = signs.Count(s => s == -1);
            int surplus = signs.Count(s => s == 1);
            int artificial = cons - slack;

            n = vars + slack + surplus + artificial + 1;
            m = cons + 1;

            T = new double[m, n];
            basis = new List<int>();

            int slackPos = vars;
            int surplusPos = vars + slack;
            int artPos = vars + slack + surplus;

            for (int i = 0; i < cons; i++)
            {
                for (int j = 0; j < vars; j++) T[i, j] = A[i, j];

                if (signs[i] == -1)
                {
                    T[i, slackPos] = 1;
                    basis.Add(slackPos++);
                }
                else if (signs[i] == 1)
                {
                    T[i, surplusPos] = -1;
                    T[i, artPos] = 1;
                    basis.Add(artPos++);
                    surplusPos++;
                }
                else
                {
                    T[i, artPos] = 1;
                    basis.Add(artPos++);
                }
                T[i, n - 1] = b[i];
            }

            int Z = m - 1;
            for (int i = 0; i < cons; i++)
            {
                if (basis[i] >= vars)
                {
                    for (int j = 0; j < n; j++)
                        T[Z, j] -= T[i, j];
                }
            }
        }

        private void BuildPhase2Objective(double[,] T, int m, int n, double[] c, bool maximize, int vars, List<int> basis)
        {
            int Z = m - 1;
            for (int j = 0; j < n; j++) T[Z, j] = 0;

            for (int j = 0; j < vars; j++)
                T[Z, j] = maximize ? -c[j] : c[j];

            for (int i = 0; i < basis.Count; i++)
            {
                int bv = basis[i];
                if (bv < vars)
                {
                    double coeff = T[Z, bv];
                    for (int j = 0; j < n; j++)
                        T[Z, j] -= coeff * T[i, j];
                }
            }
        }

        private bool SimplexProcess(double[,] T, int m, int n, List<int> basis, bool isPhase1)
        {
            while (true)
            {
                int col = FindPivotColumn(T, m, n);
                if (col == -1) return true;

                int row = FindPivotRow(T, m, n, col);
                if (row == -1)
                {
                    TxtResult.Text += "\nЗадача неограничена.\n";
                    return false;
                }

                double pivot = T[row, col];
                for (int j = 0; j < n; j++) T[row, j] /= pivot;

                for (int i = 0; i < m; i++)
                {
                    if (i == row) continue;
                    double factor = T[i, col];
                    for (int j = 0; j < n; j++)
                        T[i, j] -= factor * T[row, j];
                }

                basis[row] = col;
                PrintTable($"Итерация (pivot: строка {row}, столбец {col})", T, m, n, basis);
            }
        }

        private int FindPivotColumn(double[,] T, int m, int n)
        {
            int Z = m - 1;
            int col = -1;
            double min = 0;
            for (int j = 0; j < n - 1; j++)
            {
                if (T[Z, j] < min)
                {
                    min = T[Z, j];
                    col = j;
                }
            }
            return col;
        }

        private int FindPivotRow(double[,] T, int m, int n, int col)
        {
            int row = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < m - 1; i++)
            {
                if (T[i, col] > 1e-10)
                {
                    double ratio = T[i, n - 1] / T[i, col];
                    if (ratio < best)
                    {
                        best = ratio;
                        row = i;
                    }
                }
            }
            return row;
        }

        private void PrintTable(string title, double[,] T, int m, int n, List<int> basis)
        {
            TxtResult.Text += $"\n{title}\n\n";
            var sb = new StringBuilder();
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                    sb.Append($"{T[i, j],10:0.###}");
                sb.AppendLine();
            }
            TxtResult.Text += sb.ToString() + "\n";
        }

        /// <summary>
        /// Очищает все данные.
        /// </summary>
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in targetBoxes ?? Array.Empty<TextBox>())
                t.Text = "0";

            foreach (var row in constraintRows)
            {
                foreach (var c in row.CoeffBoxes)
                    c.Box.Text = "0";
                row.RightBox.Box.Text = "0";
                row.Sign.SelectedIndex = 0;
            }

            TxtResult.Clear();
        }

        // ====================== РАБОТА С ФАЙЛАМИ ======================

        /// <summary>
        /// Загружает задачу из текстового файла и автоматически заполняет интерфейс.
        /// </summary>
        private void BtnLoadFromFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Текстовый файл (*.txt)|*.txt",
                Title = "Выберите файл с задачей симплекс-метода"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                string[] lines = File.ReadAllLines(dlg.FileName);

                if (lines.Length < 4)
                {
                    MessageBox.Show("Неверный формат файла. Файл должен содержать минимум 4 строки.", "Ошибка формата");
                    return;
                }

                // Строка 1: количество переменных, количество ограничений
                var sizes = lines[0].Split(',').Select(int.Parse).ToArray();
                numVariables = sizes[0];
                numConstraints = sizes[1];

                TxtNumVariables.Text = numVariables.ToString();
                TxtNumConstraints.Text = numConstraints.ToString();

                // Строка 2: тип оптимизации
                string optType = lines[1].Trim();
                CbOptimize.SelectedIndex = (optType == "Минимизировать" || optType == "Минимум") ? 1 : 0;

                // Пересоздаём интерфейс под новые размеры
                GenerateUI();

                // Строка 3: коэффициенты целевой функции
                var objCoeff = lines[2].Split(',').Select(s => s.Trim()).ToArray();
                for (int j = 0; j < numVariables && j < objCoeff.Length; j++)
                {
                    targetBoxes[j].Text = objCoeff[j];
                }

                // Строки 4 и далее: ограничения
                for (int i = 0; i < numConstraints && i + 3 < lines.Length; i++)
                {
                    var parts = lines[i + 3].Split(',').Select(s => s.Trim()).ToArray();

                    if (parts.Length < numVariables + 2) continue;

                    // Коэффициенты
                    for (int j = 0; j < numVariables; j++)
                    {
                        constraintRows[i].CoeffBoxes[j].Box.Text = parts[j];
                    }

                    // Знак
                    string sign = parts[numVariables];
                    constraintRows[i].Sign.SelectedItem = sign;

                    // Правая часть
                    constraintRows[i].RightBox.Box.Text = parts[numVariables + 1];
                }

                MessageBox.Show("Задача успешно загружена из файла!", "Загрузка завершена",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при чтении файла:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Сохраняет результат решения в текстовый файл.
        /// </summary>
        private void BtnSaveResult_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtResult.Text))
            {
                MessageBox.Show("Сначала решите задачу.", "Предупреждение");
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "Текстовый файл (*.txt)|*.txt",
                FileName = $"Симплекс_решение_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, TxtResult.Text, Encoding.UTF8);
                    MessageBox.Show("Результат успешно сохранён!", "Сохранение",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}");
                }
            }
        }
    }

    // ====================== ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ ======================

    public class ConstraintRow
    {
        public StackPanel Root { get; }
        public List<NumericBox> CoeffBoxes { get; } = new List<NumericBox>();
        public ComboBox Sign { get; }
        public NumericBox RightBox { get; }

        public ConstraintRow(int vars)
        {
            Root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };

            for (int j = 0; j < vars; j++)
            {
                var nb = new NumericBox("0", 55);
                CoeffBoxes.Add(nb);
                Root.Children.Add(nb.Box);
                Root.Children.Add(new TextBlock
                {
                    Text = $"x{j + 1}" + (j < vars - 1 ? " + " : ""),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 8, 0)
                });
            }

            Sign = new ComboBox { Width = 65, Margin = new Thickness(4) };
            Sign.Items.Add("<=");
            Sign.Items.Add("=");
            Sign.Items.Add(">=");
            Sign.SelectedIndex = 0;
            Root.Children.Add(Sign);

            RightBox = new NumericBox("0", 70);
            Root.Children.Add(RightBox.Box);
        }
    }

    public class NumericBox
    {
        public TextBox Box { get; }

        public NumericBox(string defaultText, int width)
        {
            Box = new TextBox
            {
                Text = defaultText,
                Width = width,
                Margin = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
        }

        public double GetValue()
        {
            if (double.TryParse(Box.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                return val;
            return 0;
        }
    }
}