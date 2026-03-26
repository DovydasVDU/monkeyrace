namespace monkeyrace.Models
{
    // parodo klaidą, jeigu kažkas nutinka ne taip. 
    // Šis modelis yra susijęs su HomeControlleriu, kuris siunčia į view informaciją apie klaidą, kuri įvyko.
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
