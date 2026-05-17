namespace RadiopaediaConnect.Services
{
    public interface INotificationService
    {
        Task SendAsync(string subject, string body, string? jobId = null);
    }
}
