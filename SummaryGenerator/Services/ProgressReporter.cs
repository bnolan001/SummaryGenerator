namespace SummaryGenerator.Services
{
    public interface IProgress
    {
        void Report(double value);
    }

    public class ProgressReporter: IProgress
    {
        public void Report(double value)
        {
            Console.WriteLine(value);
        }
    }
}
