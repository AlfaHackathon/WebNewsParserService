using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Api.Common;

/// <summary>
/// Добавляет глобальный префикс ко всем маршрутам всех контроллеров,
/// чтобы не указывать его в каждом контроллере через [Route("...")].
/// </summary>
public class RoutePrefixConvention(string prefix) : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        // Маршруты, заданные на самом контроллере
        foreach (var selector in controller.Selectors)
        {
            selector.AttributeRouteModel = selector.AttributeRouteModel is null
                ? new AttributeRouteModel { Template = prefix }
                : new AttributeRouteModel
                {
                    Template = AttributeRouteModel.CombineTemplates(prefix, selector.AttributeRouteModel.Template)
                };
        }

        // Если на контроллере маршрутов нет — префиксуем маршруты экшенов
        if (controller.Selectors.Any(s => s.AttributeRouteModel is not null))
        {
            return;
        }

        foreach (var action in controller.Actions)
        {
            foreach (var selector in action.Selectors)
            {
                selector.AttributeRouteModel = selector.AttributeRouteModel is null
                    ? new AttributeRouteModel { Template = prefix }
                    : new AttributeRouteModel
                    {
                        Template = AttributeRouteModel.CombineTemplates(prefix, selector.AttributeRouteModel.Template)
                    };
            }
        }
    }
}
