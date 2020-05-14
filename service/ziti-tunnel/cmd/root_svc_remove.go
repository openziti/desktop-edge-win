package cmd

import (
"fmt"

"github.com/spf13/cobra"
)

func init() {
	serviceCmd.AddCommand(removeCmd)
}

var removeCmd = &cobra.Command{
	Use:   "remove",
	Short: "Removes the executable as a service.",
	Long:  `When removing the service make sure the path and service name have not changed`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("remove this thing as a service")
	},
}
