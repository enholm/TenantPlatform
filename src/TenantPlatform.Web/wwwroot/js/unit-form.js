window.tenantPlatformUnitForm = {
    filterParents: function (buildingId, selectId) {
        const select = document.getElementById(selectId);

        if (!select) {
            return;
        }

        const options = select.querySelectorAll("option[data-building-id]");

        options.forEach(option => {
            const belongsToBuilding =
                option.dataset.buildingId === buildingId;

            option.hidden = !belongsToBuilding;

            if (!belongsToBuilding && option.selected) {
                select.value = "";
            }
        });
    }
};
