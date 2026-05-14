namespace UByMoen.Api.Models.Requests;

public record SetTemperatureRequest(double Temperature);

public record SetOutletRequest(bool Active);

public record ActivatePresetRequest; // body-less, position comes from route
